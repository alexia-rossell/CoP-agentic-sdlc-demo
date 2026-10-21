namespace Demo.Agent;

public interface IWorker
{
    Task RunAsync(WorkspaceTools tools, CancellationToken cancellationToken);
}

public sealed class WorkspaceTools(Workflow workflow, RunState state, CancellationToken cancellationToken)
{
    private int calls;
    private int verifications;
    private readonly SemaphoreSlim gate = new(1, 1);

    public string Rounding => state.Rounding ?? throw new InvalidOperationException("Rounding is unresolved.");
    public string Workspace => workflow.Store.Workspace;
    public string SessionId => $"demo-{state.Id}";
    public string ContractContext => File.ReadAllText(Path.Combine(workflow.Store.Artifacts, "ticket.json"));
    public Verification Baseline => state.Before ?? throw new InvalidOperationException("Baseline has not run.");

    public void RecordExplanation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        state.Explanation = text.Length <= 8000 ? text : text[..8000];
        workflow.Store.Record(state, "sdk_explanation", "Recorded model explanation as untrusted review text, not test evidence.");
    }

    private void Guard()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++calls > 10) throw new InvalidOperationException("Tool-call budget exhausted (10).");
        workflow.ValidateBaseline(state);
    }

    public string ReadPolicy()
    {
        gate.Wait(cancellationToken);
        try
        {
            Guard();
            workflow.Store.Record(state, "read_policy", "Read only the candidate policy.");
            return File.ReadAllText(workflow.Store.PolicyPath);
        }
        finally { gate.Release(); }
    }

    public string ReplaceExpression(string original, string replacement)
    {
        gate.Wait(cancellationToken);
        try
        {
            Guard();
            if (string.IsNullOrEmpty(original) || original.Length > 200 || replacement.Length > 200)
                throw new InvalidOperationException("Supply a nonempty expression of at most 200 characters.");
            var path = workflow.Store.PolicyPath;
            workflow.Store.EnsureLocalPath(path);
            var source = File.ReadAllText(path);
            var first = source.IndexOf(original, StringComparison.Ordinal);
            if (first < 0 || source.IndexOf(original, first + original.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Patch must match exactly once. Read the current policy before editing.");
            var candidate = source.Replace(original, replacement, StringComparison.Ordinal);
            Workflow.ValidateCandidate(File.ReadAllText(Path.Combine(workflow.Store.Artifacts, "fixture-policy.cs.txt")), candidate);
            if (candidate == source) throw new InvalidOperationException("Patch does not change the candidate.");
            File.WriteAllText(path, candidate);
            workflow.Store.Record(state, "replace_expression", "Applied model-proposed bounded delay-band edit.");
            return "Applied. Run verify to obtain real evidence.";
        }
        finally { gate.Release(); }
    }

    public async Task<Verification> Verify()
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Guard();
            if (++verifications > 2) throw new InvalidOperationException("Repair verification budget exhausted (2).");
            var result = await workflow.VerifyAsync(state, $"worker-{verifications}", cancellationToken);
            workflow.Store.Record(state, "verify_tool", result.Passed ? "PASS" : "FAIL");
            return result;
        }
        finally { gate.Release(); }
    }
}

public sealed class RehearsalWorker : IWorker
{
    public async Task RunAsync(WorkspaceTools tools, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = tools.ReadPolicy();
        if (source.Contains(Workflow.ThresholdBefore, StringComparison.Ordinal))
            tools.ReplaceExpression(Workflow.ThresholdBefore, Workflow.ThresholdAfter);
        await tools.Verify();
    }
}
