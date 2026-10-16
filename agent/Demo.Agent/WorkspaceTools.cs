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
    public string SkillDirectory => Path.Combine(workflow.Store.Artifacts, "skills", "demo-policy-change");
    public string ContractContext => string.Join("\n\n", new[] { "constitution.md", "AGENTS.md", "spec.md", "plan.md", "tasks.md" }
        .Select(name => $"--- {name} (context, not expanded task authority) ---\n" +
            File.ReadAllText(Path.Combine(workflow.Store.Artifacts, name))));

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
            var allowed = (original == Workflow.ThresholdBefore && replacement == Workflow.ThresholdAfter) ||
                          (original == Workflow.RoundingBefore && replacement == Workflow.RoundingAfter);
            if (!allowed)
                throw new InvalidOperationException("Denied: only the reviewed threshold and explicit-rounding patches are allowed. No arbitrary code or paths.");
            var path = workflow.Store.PolicyPath;
            workflow.Store.EnsureLocalPath(path);
            var source = File.ReadAllText(path);
            var first = source.IndexOf(original, StringComparison.Ordinal);
            if (first < 0 || source.IndexOf(original, first + original.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Patch must match exactly once. Read the current policy before editing.");
            File.WriteAllText(path, source.Replace(original, replacement, StringComparison.Ordinal));
            workflow.Store.Record(state, "replace_expression", original == Workflow.ThresholdBefore
                ? "Applied reviewed inclusive-threshold patch." : "Applied reviewed explicit-rounding patch.");
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
        if (source.Contains(Workflow.RoundingBefore, StringComparison.Ordinal))
            tools.ReplaceExpression(Workflow.RoundingBefore, Workflow.RoundingAfter);
        await tools.Verify();
    }
}
