using System.Collections.Concurrent;
using System.Diagnostics;
using GitHub.Copilot;

namespace Demo.Agent;

public sealed record ModelUsage(string Model, double? InputTokens, double? OutputTokens,
    double? CacheReadTokens, double? CacheWriteTokens, double? CopilotBillingMultiplierCost);

public sealed record SdkUsage(List<ModelUsage> Calls)
{
    public double? InputTokens => Total(call => call.InputTokens);
    public double? OutputTokens => Total(call => call.OutputTokens);
    public double? CacheReadTokens => Total(call => call.CacheReadTokens);
    public double? CacheWriteTokens => Total(call => call.CacheWriteTokens);
    public double? CopilotBillingMultiplierCost => Total(call => call.CopilotBillingMultiplierCost);
    public string MonetaryCostStatus => "USD cost unavailable: SDK Cost is a Copilot billing multiplier, not a currency amount.";

    private double? Total(Func<ModelUsage, double?> select) =>
        Calls.Count > 0 && Calls.All(call => select(call).HasValue)
            ? Calls.Sum(select) : null;
}

public sealed class SdkUsageCollector(string model, string provider, bool copilotBilling,
    string runId, ActivityContext parent)
{
    private readonly ConcurrentDictionary<string, ModelUsage> calls = new();

    public void Record(AssistantUsageEvent usage)
    {
        var data = usage.Data;
        // The pinned SDK exposes the non-currency billing multiplier as experimental.
#pragma warning disable GHCP001
        var call = new ModelUsage(data.Model ?? model, data.InputTokens, data.OutputTokens,
            data.CacheReadTokens, data.CacheWriteTokens, copilotBilling ? data.Cost : null);
#pragma warning restore GHCP001
        var callId = string.IsNullOrWhiteSpace(data.ApiCallId) ? usage.Id.ToString() : data.ApiCallId;
        if (!calls.TryAdd(callId, call)) return;
        var end = DateTimeOffset.UtcNow;
        var start = data.Duration is { } duration && duration > TimeSpan.Zero && duration < TimeSpan.FromDays(1)
            ? end - duration : end;
        using var activity = DemoTracing.Source.StartActivity($"chat {call.Model}", ActivityKind.Client, parent,
            tags: new ActivityTagsCollection
            {
                ["demo.run_id"] = runId,
                ["gen_ai.operation.name"] = "chat",
                ["gen_ai.provider.name"] = provider,
                ["gen_ai.system"] = provider,
                ["gen_ai.request.model"] = model,
                ["gen_ai.response.model"] = call.Model,
                ["gen_ai.usage.input_tokens"] = call.InputTokens,
                ["gen_ai.usage.output_tokens"] = call.OutputTokens,
                ["gen_ai.usage.cache_read.input_tokens"] = call.CacheReadTokens,
                ["gen_ai.usage.cache_creation.input_tokens"] = call.CacheWriteTokens,
                ["demo.copilot.billing_multiplier_cost"] = call.CopilotBillingMultiplierCost
            }, startTime: start);
        activity?.SetEndTime(end.UtcDateTime);
    }

    public SdkUsage Snapshot() => new(calls.OrderBy(call => call.Key, StringComparer.Ordinal)
        .Select(call => call.Value).ToList());
}
