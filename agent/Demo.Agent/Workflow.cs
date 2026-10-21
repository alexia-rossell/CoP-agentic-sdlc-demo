using System.Text.Json;
using System.Text.RegularExpressions;

namespace Demo.Agent;

public sealed partial class Workflow(RunStore store, string dotnet)
{
    public const string ThresholdBefore = "delayMinutes > 60";
    public const string ThresholdAfter = "delayMinutes >= 60";
    public const string Question = "For CHF 10.03 at 25%, the result is 250.75 rappen. " +
        "Should fractional rappen round down, up, or nearest (half-up ties)? " +
        "Human product owner: replace the FR-004 clause in this User Story's Acceptance Criteria with exactly " +
        "'FR-004: Rounding: down', 'FR-004: Rounding: up', or 'FR-004: Rounding: nearest'. " +
        "Keep all other requirements unchanged. Comments or local answers do not count; explicit approval follows check.";

    public RunStore Store => store;

    public RunState Init(string id, WorkItem? ticket = null)
    {
        if (ticket is { Offline: false } && ticket.Type != "User Story")
            throw new InvalidOperationException("Live intake requires a User Story.");
        if (File.Exists(Path.Combine(store.DirectoryPath, "state.json")))
            throw new InvalidOperationException("This run already exists. Choose a new ID; runs are never silently reset.");
        Directory.CreateDirectory(store.Artifacts);
        Directory.CreateDirectory(store.Workspace);
        foreach (var path in new[]
        {
            "app/Claims.Service/Claims.Service.csproj",
            "app/Claims.Service/CompensationPolicy.cs",
            "app/Claims.Service/ClaimLedger.cs",
            "app/Claims.Acceptance/Claims.Acceptance.csproj",
            "app/Claims.Acceptance/Program.cs",
            "global.json"
        })
        {
            var destination = Path.Combine(store.Workspace, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(store.Root, path), destination);
        }
        File.WriteAllText(Path.Combine(store.Workspace, "Directory.Build.props"), """
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
              </PropertyGroup>
            </Project>
            """);
        ticket ??= JsonSerializer.Deserialize<WorkItem>(
            File.ReadAllText(Path.Combine(store.Root, "demo", "ticket.json")), RunStore.Json)
            ?? throw new InvalidDataException("Offline ticket is empty.");
        WriteTicket(ticket);
        File.WriteAllText(Path.Combine(store.Artifacts, "question.txt"), Question);
        var source = File.ReadAllText(store.PolicyPath);
        if (!source.Contains(ThresholdAfter, StringComparison.Ordinal))
            throw new InvalidDataException("Source fixture no longer matches the prepared demo. Review fixture preparation.");
        source = source.Replace(ThresholdAfter, ThresholdBefore, StringComparison.Ordinal);
        File.WriteAllText(store.PolicyPath, source);
        File.WriteAllText(Path.Combine(store.Artifacts, "fixture-policy.cs.txt"), source);
        var state = new RunState { Id = id };
        store.Record(state, "init", ticket.Offline
            ? "OFFLINE ticket fixture; one seeded defect in a private workspace. No ADO read."
            : $"Read ADO #{ticket.Id}, revision {ticket.Revision}; one seeded defect in a private workspace.");
        Console.WriteLine(Question);
        return state;
    }

    public void Clarify(RunState state, string rounding)
    {
        if (!ReadTicket().Offline)
            throw new InvalidOperationException("Live rounding must be read from the User Story AC with check; local clarify is offline-only.");
        RecordDecision(state, rounding, "Explicit offline per-run answer; ticket fixture remains unresolved.");
    }

    private void RecordDecision(RunState state, string rounding, string evidence)
    {
        RequireStage(state, RunStage.Draft, RunStage.AwaitTicketApproval);
        if (rounding is not ("down" or "up" or "nearest"))
            throw new ArgumentException("Rounding must be down, up or nearest; there is no default.");
        File.WriteAllText(Path.Combine(store.Artifacts, "decision.json"), JsonSerializer.Serialize(new
        {
            requirement = "FR-004",
            rounding,
            exampleFareRappen = 1003,
            exampleDelayMinutes = 60,
            expectedRappen = rounding == "down" ? 250 : 251,
            note = evidence
        }, RunStore.Json));
        state.Rounding = rounding;
        state.Stage = RunStage.AwaitTicketApproval;
        store.Record(state, "clarify", $"Recorded explicit '{rounding}' decision in this run's artifact snapshot.");
    }

    public static string? ParseRounding(string criteria)
    {
        var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(criteria, "<[^>]+>", "\n"));
        if (Regex.Matches(text, @"\bFR-004\s*:", RegexOptions.IgnoreCase).Count != 1 ||
            Regex.Matches(text, @"\bRounding\s*:", RegexOptions.IgnoreCase).Count != 1) return null;
        var answer = Regex.Match(text, @"\bFR-004\s*:\s*Rounding\s*:\s*(down|up|nearest)\s*\.?\s*(?=FR-005\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return answer.Success ? answer.Groups[1].Value.ToLowerInvariant() : null;
    }

    public async Task CheckAsync(RunState state, AdoClient ado, CancellationToken token)
    {
        var previous = ReadTicket();
        if (previous.Offline) throw new InvalidOperationException("check is live-only; use clarify for an offline fixture.");
        var current = await ado.ReadAsync(token);
        if (current.Id != previous.Id || current.Url != previous.Url || current.Type != "User Story")
            throw new InvalidOperationException("Configured ADO item is not this run's authoritative User Story.");
        var relevantChanged = current with { Revision = previous.Revision } != previous;
        if (relevantChanged)
        {
            state.TicketApproval = state.PublicationApproval = state.AdoApproval = state.ResolutionApproval = null;
            state.BugApproval = null;
            state.Before = state.After = null;
            state.CandidateHash = state.EvidenceHash = state.BaselineEvidenceHash = null;
            state.PublishedTicket = null;
            state.Rounding = null;
            state.Stage = RunStage.Draft;
            File.Copy(Path.Combine(store.Artifacts, "fixture-policy.cs.txt"), store.PolicyPath, overwrite: true);
            WriteTicket(current);
            var decision = Path.Combine(store.Artifacts, "decision.json");
            if (File.Exists(decision)) File.Delete(decision);
            store.Record(state, "refresh", $"ADO revision {current.Revision}; prior approvals and verification invalidated.");
        }
        state.CurrentTicket = current;
        store.Save(state);
        var rounding = ParseRounding(current.AcceptanceCriteria);
        if (rounding is null)
        {
            if (state.Rounding is not null) throw new InvalidOperationException("Stored decision conflicts with unresolved ADO criteria.");
            store.Record(state, "check", "FR-004 unresolved/ambiguous. Human must set exactly 'FR-004: Rounding: down|up|nearest'. No approval granted.");
            return;
        }
        if (state.Rounding is null)
            RecordDecision(state, rounding, $"Authoritative User Story {current.Url}; revision {current.Revision}; exact FR-004 answer read from AC.");
        store.Record(state, "check", $"Confirmed FR-004 '{rounding}' in ADO revision {current.Revision}; no approval inferred.");
    }

    public void Approve(RunState state, string by)
    {
        RequireStage(state, RunStage.AwaitTicketApproval);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        if (state.Rounding is null) throw new InvalidOperationException("Clarification is required.");
        state.TicketApproval = new(by, DateTimeOffset.UtcNow, store.BaselineHash());
        state.Stage = RunStage.Approved;
        store.Record(state, "approve", $"Presenter '{by}' confirmed ticket scope, rounding answer and protected acceptance runner.");
    }

    public void ValidateBaseline(RunState state)
    {
        if (state.TicketApproval is null || state.TicketApproval.Hash != store.BaselineHash())
            throw new InvalidOperationException("Approved artifacts or test oracle changed. Refusing execution/export; start a new reviewed run.");
        using var decision = JsonDocument.Parse(File.ReadAllText(Path.Combine(store.Artifacts, "decision.json")));
        if (decision.RootElement.GetProperty("rounding").GetString() != state.Rounding)
            throw new InvalidOperationException("Workflow rounding does not match the approved decision.");
        store.EnsureLocalPath(store.PolicyPath);
        var candidate = File.ReadAllText(store.PolicyPath);
        var baseline = File.ReadAllText(Path.Combine(store.Artifacts, "fixture-policy.cs.txt"));
        ValidateCandidate(baseline, candidate);
    }

    public static void ValidateCandidate(string baseline, string candidate)
    {
        var before = PolicyExpression().Match(baseline);
        var after = PolicyExpression().Match(candidate);
        if (!before.Success || !after.Success ||
            PolicyExpression().Matches(candidate).Count != 1 ||
            baseline[..before.Index] != candidate[..after.Index] ||
            baseline[(before.Index + before.Length)..] != candidate[(after.Index + after.Length)..])
            throw new InvalidOperationException("Only the bounded delay-band expression may change; no arbitrary code, validation, rounding or test edits.");
    }

    [GeneratedRegex(@"delayMinutes (?:>=|>) [0-9]{1,7} \? (?:0|25|50) : delayMinutes (?:>=|>) [0-9]{1,7} \? (?:0|25|50) : (?:0|25|50)", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyExpression();

    public WorkItem ReadTicket() => JsonSerializer.Deserialize<WorkItem>(
        File.ReadAllText(Path.Combine(store.Artifacts, "ticket.json")), RunStore.Json)
        ?? throw new InvalidDataException("Ticket snapshot is empty.");

    private void WriteTicket(WorkItem ticket) => File.WriteAllText(Path.Combine(store.Artifacts, "ticket.json"),
        JsonSerializer.Serialize(ticket, RunStore.Json));

    public async Task AskAsync(RunState state, AdoClient ado, string by, CancellationToken cancellationToken)
    {
        RequireStage(state, RunStage.Draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        var ticket = await ado.AddHistoryAsync(ReadTicket(), $"Demo run {state.Id}; question approved by {by}: {Question}", cancellationToken);
        WriteTicket(ticket);
        store.Record(state, "ask", $"Posted rounding question to ADO #{ticket.Id}, revision {ticket.Revision}.");
    }

    public async Task PublishAsync(RunState state, AdoClient ado, string by, CancellationToken cancellationToken)
    {
        RequireStage(state, RunStage.Exported);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        ValidateBaseline(state);
        ValidateEvidence(state);
        var ticket = state.CurrentTicket ?? ReadTicket();
        var approval = new Approval(by, DateTimeOffset.UtcNow, state.CandidateHash!);
        var updated = await ado.AddHistoryAsync(ticket,
            $"Demo run {state.Id}; publication approved by {by}. Mode: {state.Mode}. " +
            $"Product-owner rounding answer: {state.Rounding}. Baseline failed; candidate passed {state.After!.Results.Count} checks. " +
            $"Candidate SHA-256: {state.CandidateHash}. Linked Bug: {state.Bug?.Id}. Local evidence exported. No code push, PR, merge or deployment.",
            cancellationToken);
        state.PublishedTicket = updated;
        state.AdoApproval = approval;
        state.Stage = RunStage.Published;
        store.Record(state, "publish", $"Posted verified result to ADO #{ticket.Id}.");
    }

    public async Task ExecuteAsync(RunState state, IWorker worker, string mode, CancellationToken cancellationToken)
    {
        RequireStage(state, RunStage.Approved, RunStage.BaselineFailed, RunStage.Failed, RunStage.Executing);
        ValidateBaseline(state);
        if (!ReadTicket().Offline && (state.Before is not { Passed: false } || state.Bug is null || state.BugApproval is null))
            throw new InvalidOperationException("Live repair requires test first, then explicitly approved open-bug. execute never writes to ADO.");
        if (!ReadTicket().Offline && state.BaselineEvidenceHash != BaselineEvidenceHash())
            throw new InvalidOperationException("Baseline evidence changed after Bug approval.");
        state.Mode = mode;
        state.Stage = RunStage.Executing;
        state.Error = null;
        state.After = null;
        state.CandidateHash = null;
        store.Record(state, "execute", mode == "rehearsal"
            ? "OFFLINE REHEARSAL: deterministic tool selection, real compilation/tests, no model."
            : "LIVE SDK: model selects from bounded custom tools.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            if (state.Before is null)
            {
                state.Before = await VerifyAsync(state, "baseline", timeout.Token);
                store.Record(state, "baseline", state.Before.Passed
                    ? "Unexpectedly green baseline; refusing a red-to-green claim."
                    : "Real acceptance failures captured before repair.");
            }
            if (state.Before.Passed)
                throw new InvalidOperationException("Prepared baseline unexpectedly passed; no red-to-green claim is possible.");
            var tools = new WorkspaceTools(this, state, timeout.Token);
            await worker.RunAsync(tools, timeout.Token);
            ValidateBaseline(state);
            state.After = await VerifyAsync(state, "candidate", timeout.Token);
            if (!state.After.Passed) throw new InvalidOperationException("Candidate verification failed. Inspect candidate-results.json.");
            state.CandidateHash = store.HashFile(store.PolicyPath);
            state.EvidenceHash = EvidenceHash();
            state.Stage = RunStage.AwaitPublicationApproval;
            store.Record(state, "verify", "PASS: candidate verified; export still requires separate approval.");
        }
        catch (Exception error)
        {
            state.Stage = RunStage.Failed;
            state.Error = error.Message;
            store.Record(state, "failed", error.Message);
            throw;
        }
    }

    public async Task TestBaselineAsync(RunState state, CancellationToken token)
    {
        RequireStage(state, RunStage.Approved, RunStage.BaselineFailed);
        ValidateBaseline(state);
        if (store.HashFile(store.PolicyPath) != store.HashFile(Path.Combine(store.Artifacts, "fixture-policy.cs.txt")))
            throw new InvalidOperationException("Baseline must be the unchanged seeded workspace, not a candidate.");
        state.Before = await VerifyAsync(state, "baseline", token);
        state.Stage = state.Before.Passed ? RunStage.Failed : RunStage.BaselineFailed;
        store.Record(state, "baseline", state.Before.Passed ? "Unexpectedly green; no Bug may be opened." : "Real failing baseline captured; open-bug needs separate remote approval.");
        if (state.Before.Passed) throw new InvalidOperationException("Baseline unexpectedly passed.");
        state.BaselineEvidenceHash = BaselineEvidenceHash();
        store.Save(state);
    }

    public async Task OpenBugAsync(RunState state, AdoClient ado, string by, bool approved, CancellationToken token)
    {
        RequireStage(state, RunStage.BaselineFailed);
        ValidateBaseline(state);
        if (state.BaselineEvidenceHash != BaselineEvidenceHash())
            throw new InvalidOperationException("Baseline evidence changed.");
        state.Bug = await new ManagedDemo(store).OpenBugAsync(ado, state.CurrentTicket ?? ReadTicket(), state, by, approved, token);
        state.BugApproval = new(by, DateTimeOffset.UtcNow, state.BaselineEvidenceHash);
        store.Record(state, "open-bug", $"Tracked linked Bug #{state.Bug.Id}; requirement remains User Story #{ReadTicket().Id}.");
    }

    private string EvidenceHash()
    {
        var entries = new[] { "baseline-results.json", "candidate-results.json", "baseline-output.log", "candidate-output.log" }
            .Select(name => store.HashFile(Path.Combine(store.DirectoryPath, name)));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", entries))));
    }

    private string BaselineEvidenceHash() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(
            store.HashFile(Path.Combine(store.DirectoryPath, "baseline-results.json")) + "\n" +
            store.HashFile(Path.Combine(store.DirectoryPath, "baseline-output.log")))));

    public void ValidateEvidence(RunState state)
    {
        if (state.After?.Passed != true || state.Before?.Passed != false ||
            state.CandidateHash != store.HashFile(store.PolicyPath) || state.EvidenceHash != EvidenceHash())
            throw new InvalidOperationException("Candidate or verification evidence changed or is not verified.");
    }

    public async Task ResolveBugAsync(RunState state, AdoClient ado, string by, bool acknowledgeLocal, CancellationToken token)
    {
        ManagedDemo.RequireApproval(by, acknowledgeLocal);
        RequireStage(state, RunStage.Published);
        ValidateBaseline(state);
        ValidateEvidence(state);
        if (state.ResolutionApproval is not null || state.Bug is null)
            throw new InvalidOperationException("No unresolved tracked Bug for this run.");
        var journal = new ManagedDemo(store).Read(ado);
        if (journal.Resetting || !journal.BugIds.Contains(state.Bug.Id))
            throw new InvalidOperationException("Bug is not an active tracked demo-created item.");
        ado.VerifyOwnership(state.Bug, ManagedDemo.Marker(journal, "bug"), "Bug");
        if (await ado.ForItem(ReadTicket().Id).ReadAsync(token) != state.PublishedTicket)
            throw new InvalidOperationException("Requirement changed since publication; check and reapprove.");
        state.Bug = await ado.ForItem(state.Bug.Id).ResolveAsync(state.Bug,
            $"Resolution approved by {by}: synthetic demo defect verified ONLY in a local candidate. " +
            $"Not committed, integrated, merged or deployed. Run {state.Id}; mode {state.Mode}; " +
            $"candidate {state.CandidateHash}; {state.After!.Results.Count} checks passed. Requirement {ReadTicket().Url}.", token);
        state.ResolutionApproval = new(by, DateTimeOffset.UtcNow, state.CandidateHash!);
        store.Record(state, "resolve-bug", $"Bug #{state.Bug.Id}: Resolved for this synthetic local-candidate demo only; not deployed.");
    }
    public async Task<Verification> VerifyAsync(RunState state, string label, CancellationToken cancellationToken)
    {
        ValidateBaseline(state);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var result = await ProcessRunner.RunAsync(dotnet,
            ["run", "--project", Path.Combine(store.Workspace, "app", "Claims.Acceptance"),
                "--configuration", "Release", "--", state.Rounding!], store.Workspace, timeout.Token);
        File.WriteAllText(Path.Combine(store.DirectoryPath, $"{label}-output.log"), result.Output + result.Error);
        var json = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.StartsWith("{\"passed\":", StringComparison.Ordinal));
        if (json is null || result.ExitCode is not (0 or 1))
            throw new InvalidOperationException($"Acceptance runner did not complete (exit {result.ExitCode}). Inspect {label}-output.log.");
        var verification = JsonSerializer.Deserialize<Verification>(json, RunStore.Json)
            ?? throw new InvalidDataException("Acceptance report was empty.");
        if (verification.Results.Count != 12 || verification.Passed != verification.Results.All(item => item.Passed) ||
            verification.Passed != (result.ExitCode == 0) ||
            !string.Equals(verification.Rounding, state.Rounding, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Acceptance report shape or exit status does not match the contract.");
        File.WriteAllText(Path.Combine(store.DirectoryPath, $"{label}-results.json"),
            JsonSerializer.Serialize(verification, RunStore.Json));
        foreach (var item in verification.Results)
            Console.WriteLine($"  {(item.Passed ? "PASS" : "FAIL")} {item.Requirement}: {item.Name}{(item.Error is null ? "" : $" - {item.Error}")}");
        ValidateBaseline(state);
        return verification;
    }

    public void Export(RunState state, string by)
    {
        RequireStage(state, RunStage.AwaitPublicationApproval, RunStage.Exported);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        ValidateBaseline(state);
        ValidateEvidence(state);
        state.PublicationApproval ??= new(by, DateTimeOffset.UtcNow, state.CandidateHash!);
        var output = Path.Combine(store.DirectoryPath, "export");
        store.EnsureLocalPath(output);
        Directory.CreateDirectory(output);
        store.EnsureLocalPath(Path.Combine(output, "CompensationPolicy.cs"));
        store.EnsureLocalPath(Path.Combine(output, "evidence.json"));
        File.Copy(store.PolicyPath, Path.Combine(output, "CompensationPolicy.cs"), overwrite: true);
        foreach (var name in new[] { "baseline-output.log", "candidate-output.log", "baseline-results.json", "candidate-results.json" })
        {
            store.EnsureLocalPath(Path.Combine(output, name));
            File.Copy(Path.Combine(store.DirectoryPath, name), Path.Combine(output, name), overwrite: true);
        }
        store.EnsureLocalPath(Path.Combine(output, "before-policy.cs.txt"));
        File.Copy(Path.Combine(store.Artifacts, "fixture-policy.cs.txt"), Path.Combine(output, "before-policy.cs.txt"), overwrite: true);
        store.EnsureLocalPath(Path.Combine(output, "ticket.json"));
        File.Copy(Path.Combine(store.Artifacts, "ticket.json"), Path.Combine(output, "ticket.json"), overwrite: true);
        store.EnsureLocalPath(Path.Combine(output, "review.md"));
        var rows = state.After!.Results.Select(item =>
            $"| {item.Requirement} | {item.Name} | {(item.Passed ? "PASS" : "FAIL")} |");
        File.WriteAllText(Path.Combine(output, "review.md"), $"""
            # Local demo evidence: {state.Id}

            Mode: {state.Mode}. Rounding: {state.Rounding}.
            Ticket approval: {state.TicketApproval!.Hash}.
            Candidate: {state.CandidateHash}.

            Compare `before-policy.cs.txt` with `CompensationPolicy.cs`.
            Only the delay-band expression is editable.
            Rounding, validation, claim ledger and acceptance suite are unchanged.

            | Requirement | Reviewed acceptance case | Result |
            |---|---|---|
            {string.Join("\n", rows)}

            Verification command: `dotnet run --project workspace/app/Claims.Acceptance --configuration Release -- {state.Rounding}`.
            See the raw logs and JSON reports. The candidate passed the protected
            suite; this is not proof of general correctness.

            Local presenter approval is self-asserted, not enterprise identity verification.
            Export is local. A separate publish command may post results to ADO.
            Hosting status is not inferred from these tests.
            """);
        state.Stage = RunStage.Exported;
        store.Record(state, "export", "Local evidence bundle only; no ADO PR, push, merge or deployment performed.");
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(state, RunStore.Json));
    }

    private static void RequireStage(RunState state, params RunStage[] allowed)
    {
        if (!allowed.Contains(state.Stage))
            throw new InvalidOperationException($"Operation is not allowed in {state.Stage}. Expected: {string.Join(", ", allowed)}.");
    }
}
