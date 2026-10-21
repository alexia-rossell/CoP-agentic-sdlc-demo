using System.Text.Json;
using Demo.Agent;
using GitHub.Copilot;
using Xunit;

namespace Demo.Tests;

public sealed class SdkUsageTests
{
    [Fact]
    public void Usage_is_deduplicated_by_api_call_and_does_not_count_context_occupancy()
    {
        var collector = new SdkUsageCollector("model", "openai", false, "test", default);
        var first = Usage("api-1", 100, 10, 20);
        collector.Record(first);
        collector.Record(first);
        collector.Record(Usage("api-1", 100, 10, 20));
        collector.Record(Usage("api-2", 200, 30, 40));
        var result = collector.Snapshot();
        Assert.Equal(2, result.Calls.Count);
        Assert.Equal(300, result.InputTokens);
        Assert.Equal(40, result.OutputTokens);
        Assert.Equal(60, result.CacheReadTokens);
        Assert.Null(result.CopilotBillingMultiplierCost);
        Assert.Contains("not a currency", result.MonetaryCostStatus);
    }

    [Fact]
    public void Missing_or_partial_usage_is_unavailable_not_zero()
    {
        var collector = new SdkUsageCollector("model", "openai", false, "test", default);
        Assert.Null(collector.Snapshot().InputTokens);
        Assert.Null(collector.Snapshot().OutputTokens);
        collector.Record(Usage(null, 100, 10, null));
        collector.Record(Usage(null, null, 20, null));
        var result = collector.Snapshot();
        Assert.Equal(2, result.Calls.Count);
        Assert.Null(result.InputTokens);
        Assert.Equal(30, result.OutputTokens);
        Assert.Null(result.CacheReadTokens);
        Assert.Null(result.CacheWriteTokens);
    }

    [Fact]
    public void Copilot_cost_is_preserved_only_as_non_currency_multiplier()
    {
        var collector = new SdkUsageCollector("model", "github.copilot", true, "test", default);
        collector.Record(Usage("api-1", 100, 10, null));
        collector.Record(Usage("api-2", 100, 10, null));
        Assert.Equal(3, collector.Snapshot().CopilotBillingMultiplierCost);
        Assert.Contains("USD cost unavailable", collector.Snapshot().MonetaryCostStatus);
    }

    private static AssistantUsageEvent Usage(string? apiCallId, double? input, double? output, double? cached) =>
        Assert.IsType<AssistantUsageEvent>(SessionEvent.FromJson(JsonSerializer.Serialize(new
        {
            type = "assistant.usage", id = Guid.NewGuid().ToString(),
            timestamp = DateTimeOffset.UtcNow, parentId = (string?)null,
            data = new { apiCallId, model = "model", inputTokens = input, outputTokens = output,
                cacheReadTokens = cached, cost = 1.5, duration = 12 }
        })));
}
