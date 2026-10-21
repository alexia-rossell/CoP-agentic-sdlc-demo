namespace Demo.Agent;

public sealed record RepairEvidence(string BeforeExpression, string AfterExpression)
{
    public string Diff => $"""
        --- before-policy.cs.txt
        +++ CompensationPolicy.cs
        @@ delay-band expression @@
        -{BeforeExpression}
        +{AfterExpression}
        """;

    public string Findings(RunState state, WorkItem story)
    {
        if (state.Before is not { Passed: false } before || state.After is not { Passed: true } after)
            throw new InvalidOperationException("Findings require a real failing baseline and a passing candidate.");
        var failures = before.Results.Where(result => !result.Passed)
            .Select(result => $"- {result.Requirement}: {result.Name}: {result.Error}");
        return $"""
            Repair findings - run {state.Id}

            Repair performed by: {(state.Mode == "sdk" ? "GitHub Copilot SDK agent (mode: sdk)" : $"Scripted rehearsal, no model inference (mode: {state.Mode})")}.
            Requirement: {story.Url}
            Linked Bug: {state.Bug?.Url}
            Product-owner rounding answer: {state.Rounding}.

            Actual baseline: {before.Results.Count(result => !result.Passed)}/{before.Results.Count} checks failed.
            {string.Join("\n", failures)}

            Proposed code fix - CompensationPolicy.Calculate (verified private-workspace candidate):
            Actual diff from the protected before/after source:
            {Diff}

            Agent explanation (model-provided review text, not test evidence):
            {state.Explanation ?? "No model explanation recorded."}

            Verification: candidate passed {after.Results.Count} checks.
            Rounding, validation, duplicate handling and acceptance tests are unchanged.
            Candidate SHA-256: {state.CandidateHash}.
            SDK trace ID: {state.TraceId ?? "unavailable"}.
            Verified only in the private workspace. Not committed, integrated, merged or deployed.
            No code push, PR, merge or deployment. Bug status is not changed by publishing findings.
            """;
    }
}
