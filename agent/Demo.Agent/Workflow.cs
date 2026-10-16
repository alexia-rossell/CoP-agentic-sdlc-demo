using System.Text.Json;

namespace Demo.Agent;

public sealed class Workflow(RunStore store, string dotnet)
{
    public const string ThresholdBefore = "delayMinutes > 60";
    public const string ThresholdAfter = "delayMinutes >= 60";
    public const string RoundingBefore = "return (numerator + 50) / 100;";
    public const string RoundingAfter = """
        return rounding switch
                {
                    Rounding.Down => numerator / 100,
                    Rounding.Up => (numerator + 99) / 100,
                    Rounding.Nearest => (numerator + 50) / 100,
                    _ => throw new ArgumentOutOfRangeException(nameof(rounding))
                };
        """;
    private const string Clarification = "[NEEDS CLARIFICATION: how should fractional-rappen compensation be rounded?]";

    public RunStore Store => store;

    public RunState Init(string id)
    {
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
        foreach (var name in new[] { "spec.md", "plan.md", "tasks.md" })
            File.Copy(Path.Combine(store.Root, "specs", "001-delay-compensation", name), Path.Combine(store.Artifacts, name));
        File.Copy(Path.Combine(store.Root, ".specify", "memory", "constitution.md"),
            Path.Combine(store.Artifacts, "constitution.md"));
        File.Copy(Path.Combine(store.Root, "AGENTS.md"), Path.Combine(store.Artifacts, "AGENTS.md"));
        var skillPath = Path.Combine(store.Artifacts, "skills", "demo-policy-change");
        Directory.CreateDirectory(skillPath);
        File.Copy(Path.Combine(store.Root, ".github", "skills", "demo-policy-change", "SKILL.md"),
            Path.Combine(skillPath, "SKILL.md"));
        var source = File.ReadAllText(store.PolicyPath);
        if (!source.Contains(ThresholdAfter, StringComparison.Ordinal) || !source.Contains(RoundingAfter, StringComparison.Ordinal))
            throw new InvalidDataException("Source fixture no longer matches the prepared demo. Review fixture preparation.");
        source = source.Replace(ThresholdAfter, ThresholdBefore, StringComparison.Ordinal)
            .Replace(RoundingAfter, RoundingBefore, StringComparison.Ordinal);
        File.WriteAllText(store.PolicyPath, source);
        File.WriteAllText(Path.Combine(store.Artifacts, "fixture-policy.cs.txt"), source);
        var state = new RunState { Id = id };
        store.Record(state, "init", "Prepared two labelled fixture defects in a private run workspace; repository source unchanged.");
        return state;
    }

    public void Clarify(RunState state, string rounding)
    {
        RequireStage(state, RunStage.Draft, RunStage.AwaitSpecApproval);
        if (rounding is not ("down" or "up" or "nearest"))
            throw new ArgumentException("Rounding must be down, up or nearest; there is no default.");
        var original = File.ReadAllText(Path.Combine(store.Root, "specs", "001-delay-compensation", "spec.md"));
        if (!original.Contains(Clarification, StringComparison.Ordinal))
            throw new InvalidDataException("The expected draft clarification marker is missing. Review the demo input.");
        var resolved = original.Replace(Clarification,
            $"Demo-run decision: round {rounding} to whole rappen (nearest uses half-up).", StringComparison.Ordinal)
            .Replace("Draft; clarification and approval pending",
                "Demo-run contract; approval recorded separately in workflow state", StringComparison.Ordinal)
            .Replace("Pending FR-004 clarification", rounding == "down" ? "250" : "251", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(store.Artifacts, "spec.md"), resolved);
        File.WriteAllText(Path.Combine(store.Artifacts, "decision.json"), JsonSerializer.Serialize(new
        {
            requirement = "FR-004",
            rounding,
            exampleFareRappen = 1003,
            exampleDelayMinutes = 60,
            expectedRappen = rounding == "down" ? 250 : 251,
            note = "Run-specific demo decision; repository draft deliberately remains unresolved."
        }, RunStore.Json));
        state.Rounding = rounding;
        state.Stage = RunStage.AwaitSpecApproval;
        store.Record(state, "clarify", $"Recorded explicit '{rounding}' decision in this run's artifact snapshot.");
    }

    public void Approve(RunState state, string by)
    {
        RequireStage(state, RunStage.AwaitSpecApproval);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        if (state.Rounding is null) throw new InvalidOperationException("Clarification is required.");
        state.SpecApproval = new(by, DateTimeOffset.UtcNow, store.BaselineHash());
        state.Stage = RunStage.Approved;
        store.Record(state, "approve", $"Local presenter '{by}' approved this demo baseline and acceptance runner.");
    }

    public void ValidateBaseline(RunState state)
    {
        if (state.SpecApproval is null || state.SpecApproval.Hash != store.BaselineHash())
            throw new InvalidOperationException("Approved artifacts or test oracle changed. Refusing execution/export; start a new reviewed run.");
        using var decision = JsonDocument.Parse(File.ReadAllText(Path.Combine(store.Artifacts, "decision.json")));
        if (decision.RootElement.GetProperty("rounding").GetString() != state.Rounding)
            throw new InvalidOperationException("Workflow rounding does not match the approved decision.");
        store.EnsureLocalPath(store.PolicyPath);
        var candidate = File.ReadAllText(store.PolicyPath);
        var baseline = File.ReadAllText(Path.Combine(store.Artifacts, "fixture-policy.cs.txt"));
        var allowed = new[]
        {
            baseline,
            baseline.Replace(ThresholdBefore, ThresholdAfter, StringComparison.Ordinal),
            baseline.Replace(RoundingBefore, RoundingAfter, StringComparison.Ordinal),
            baseline.Replace(ThresholdBefore, ThresholdAfter, StringComparison.Ordinal)
                .Replace(RoundingBefore, RoundingAfter, StringComparison.Ordinal)
        };
        if (!allowed.Contains(candidate, StringComparer.Ordinal))
            throw new InvalidOperationException("Candidate contains changes outside the two reviewed patches.");
    }

    public async Task ExecuteAsync(RunState state, IWorker worker, string mode, CancellationToken cancellationToken)
    {
        RequireStage(state, RunStage.Approved, RunStage.Failed, RunStage.Executing);
        ValidateBaseline(state);
        state.Mode = mode;
        state.Stage = RunStage.Executing;
        state.Error = null;
        state.After = null;
        state.CandidateHash = null;
        store.Record(state, "execute", mode == "rehearsal"
            ? "OFFLINE REHEARSAL: deterministic tool selection, real compilation/tests, no model."
            : "LIVE SDK: model selects from bounded custom tools.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
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
        if (state.CandidateHash != store.HashFile(store.PolicyPath) || state.After?.Passed != true)
            throw new InvalidOperationException("Candidate changed or is not verified. Refusing export.");
        state.PublicationApproval ??= new(by, DateTimeOffset.UtcNow, state.CandidateHash);
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
        store.EnsureLocalPath(Path.Combine(output, "spec.md"));
        File.Copy(Path.Combine(store.Artifacts, "spec.md"), Path.Combine(output, "spec.md"), overwrite: true);
        store.EnsureLocalPath(Path.Combine(output, "review.md"));
        var rows = state.After.Results.Select(item =>
            $"| {item.Requirement} | {item.Name} | {(item.Passed ? "PASS" : "FAIL")} |");
        File.WriteAllText(Path.Combine(output, "review.md"), $"""
            # Local demo evidence: {state.Id}

            Mode: {state.Mode}. Rounding: {state.Rounding}.
            Baseline approval: {state.SpecApproval!.Hash}.
            Candidate: {state.CandidateHash}.

            Compare `before-policy.cs.txt` with `CompensationPolicy.cs`.
            The two reviewed edits are the inclusive 60-minute threshold and
            explicit rounding. Claim ledger and acceptance suite are unchanged.

            | Requirement | Reviewed acceptance case | Result |
            |---|---|---|
            {string.Join("\n", rows)}

            Verification command: `dotnet run --project workspace/app/Claims.Acceptance --configuration Release -- {state.Rounding}`.
            See the raw logs and JSON reports. The candidate passed the protected
            suite; this is not proof of general correctness or a Spec Kit convergence assessment.

            Local presenter approval is self-asserted, not enterprise identity verification.
            No ADO PR or Foundry deployment was performed by this application.
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
