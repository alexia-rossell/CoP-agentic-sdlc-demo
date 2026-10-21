using System.Text;
using System.Text.Json;

namespace Demo.Agent;

public static class DemoOutput
{
    public static string Format(JsonElement response)
    {
        var state = response.GetProperty("state").Deserialize<RunState>(RunStore.Json)
            ?? throw new InvalidDataException("Hosted response has no run state.");
        var text = new StringBuilder();
        text.AppendLine($"Run {state.Id} | {state.Stage}");
        if (state.CurrentTicket is { } story)
            text.AppendLine($"Story #{story.Id}: {story.Url}");
        if (state.Bug is { } bug)
            text.AppendLine($"Bug #{bug.Id}: {bug.Url}");
        if (state.Rounding is not null)
            text.AppendLine($"PO rounding decision: {state.Rounding}");
        if (state.Before is { } before)
        {
            text.AppendLine();
            text.AppendLine($"BASELINE: {before.Results.Count(result => !result.Passed)}/{before.Results.Count} checks failed");
            foreach (var failure in before.Results.Where(result => !result.Passed))
                text.AppendLine($"  FAIL {failure.Requirement}: {failure.Name} - {failure.Error}");
        }
        if (state.Mode is not null)
        {
            text.AppendLine();
            text.AppendLine(state.Mode == "sdk"
                ? "REPAIR: GitHub Copilot SDK agent"
                : $"REPAIR: scripted rehearsal (mode: {state.Mode}; no model inference)");
        }
        if (response.TryGetProperty("repair", out var repair) && repair.ValueKind == JsonValueKind.Object)
        {
            var change = repair.Deserialize<RepairEvidence>(RunStore.Json)
                ?? throw new InvalidDataException("Hosted response has empty repair evidence.");
            text.AppendLine("ACTUAL CODE CHANGE:");
            text.AppendLine(change.Diff);
        }
        else if (response.TryGetProperty("candidatePolicy", out var candidate) &&
                 candidate.ValueKind == JsonValueKind.String)
        {
            text.AppendLine("CANDIDATE SOURCE (this deployed version did not return a before/after diff):");
            text.AppendLine(candidate.GetString());
        }
        if (!string.IsNullOrWhiteSpace(state.Explanation))
        {
            text.AppendLine();
            text.AppendLine("SDK EXPLANATION (model-provided review text, not test evidence):");
            text.AppendLine(state.Explanation);
        }
        if (state.After is { } after)
        {
            text.AppendLine();
            text.AppendLine($"VERIFICATION: {(after.Passed ? "PASS" : "FAIL")} - " +
                $"{after.Results.Count(result => result.Passed)}/{after.Results.Count} protected checks passed");
            text.AppendLine($"Candidate SHA-256: {state.CandidateHash}");
            text.AppendLine("Private-workspace candidate only; not committed, merged or deployed.");
        }
        if (state.BugFindingsApproval is not null)
            text.AppendLine("Findings posted to the linked Bug. Bug status unchanged.");
        if (state.Usage is { } usage)
        {
            text.AppendLine();
            text.AppendLine($"SDK USAGE: {usage.Calls.Count} reported model calls");
            text.AppendLine($"Input tokens: {usage.InputTokens?.ToString() ?? "unavailable"}; " +
                $"output tokens: {usage.OutputTokens?.ToString() ?? "unavailable"}; " +
                $"cache-read tokens: {usage.CacheReadTokens?.ToString() ?? "unavailable"}");
            if (usage.CopilotBillingMultiplierCost is { } cost)
                text.AppendLine($"Copilot billing multiplier cost (not currency): {cost}");
            text.AppendLine(usage.MonetaryCostStatus);
        }
        else if (state.Mode == "sdk")
            text.AppendLine("SDK token usage unavailable: this run did not record usage events.");
        if (state.TraceId is not null)
            text.AppendLine($"Foundry/Application Insights trace ID: {state.TraceId}");
        if (state.Stage == RunStage.Published)
            text.AppendLine("Verified evidence posted to the Story.");
        if (response.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
        {
            text.AppendLine();
            text.AppendLine(message.GetString());
        }
        return string.Concat(text.ToString().Where(character =>
            !char.IsControl(character) || character is '\n' or '\t'));
    }
}
