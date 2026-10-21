using System.Text.Json;
using Demo.Agent;
using Xunit;

namespace Demo.Tests;

public sealed class DemoOutputTests
{
    [Fact]
    public void Presenter_sees_actual_change_links_failures_verification_and_usage_without_event_dump()
    {
        var state = new RunState
        {
            Id = "test", Stage = RunStage.Exported, Mode = "sdk", Rounding = "nearest",
            CurrentTicket = new(3, 1, "https://example.test/story/3", "Story", "", "", "User Story"),
            Bug = new(4, 1, "https://example.test/bug/4", "Bug", "", "", "Bug"),
            Before = new(false, "nearest", [new("FR-002", "Boundary at 60 minutes", false, "Expected 1500, got 0")]),
            After = new(true, "nearest", [new("FR-002", "Boundary at 60 minutes", true, null)]),
            CandidateHash = "candidate-hash", Explanation = "Model review text",
            Usage = new([new("model", 100, 10, 20, null, null)]), TraceId = "trace-id",
            Events = [new(DateTimeOffset.UtcNow, "Executing", "internal", "Do not dump internal checkpoints")]
        };
        var repair = new RepairEvidence("delayMinutes > 60 ? 25 : 0", "delayMinutes >= 60 ? 25 : 0");
        var output = DemoOutput.Format(JsonSerializer.SerializeToElement(new
            { state, repair, message = "Review before publication" }, RunStore.Json));
        Assert.Contains("Story #3: https://example.test/story/3", output);
        Assert.Contains("Bug #4: https://example.test/bug/4", output);
        Assert.Contains("GitHub Copilot SDK agent", output);
        Assert.Contains("Expected 1500, got 0", output);
        Assert.Contains("-delayMinutes > 60 ? 25 : 0", output);
        Assert.Contains("+delayMinutes >= 60 ? 25 : 0", output);
        Assert.Contains("1/1 protected checks passed", output);
        Assert.Contains("not test evidence", output);
        Assert.Contains("not committed, merged or deployed", output);
        Assert.Contains("Input tokens: 100; output tokens: 10; cache-read tokens: 20", output);
        Assert.Contains("USD cost unavailable", output);
        Assert.Contains("trace-id", output);
        Assert.DoesNotContain("Do not dump internal checkpoints", output);
    }

    [Fact]
    public void Older_server_output_does_not_invent_a_diff_or_usage()
    {
        var output = DemoOutput.Format(JsonSerializer.SerializeToElement(new
        {
            state = new RunState { Id = "test", Mode = "sdk" },
            candidatePolicy = "actual candidate source\u001b"
        }, RunStore.Json));
        Assert.Contains("did not return a before/after diff", output);
        Assert.Contains("actual candidate source", output);
        Assert.Contains("SDK token usage unavailable", output);
        Assert.DoesNotContain("ACTUAL CODE CHANGE:", output);
        Assert.DoesNotContain('\u001b', output);
    }
}
