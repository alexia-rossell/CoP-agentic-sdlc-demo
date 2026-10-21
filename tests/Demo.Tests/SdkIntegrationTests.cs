using System.Net;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Demo.Agent;
using GitHub.Copilot;
using Xunit;

namespace Demo.Tests;

public sealed class SdkIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_sdk_runtime_reports_usage_and_traces_for_success_and_failed_candidates(bool failingCandidate)
    {
        using var request = new Activity("test.request").Start();
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DemoTracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == request.TraceId) spans.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);
        using var fixture = new DemoFixture();
        await using var provider = new ScriptedProvider(failingCandidate
            ? "delayMinutes > 61" : Workflow.ThresholdAfter);
        var state = fixture.Approved();
        var worker = new SdkWorker(new ModelSettings("gpt-4.1", new ProviderConfig
        {
            Type = "openai",
            BaseUrl = provider.Url,
            WireApi = "completions",
            ApiKey = "local-test-only"
        }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        if (failingCandidate)
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workflow.ExecuteAsync(state, worker, "sdk", timeout.Token));
        else
            await fixture.Workflow.ExecuteAsync(state, worker, "sdk", timeout.Token);
        Assert.Equal(failingCandidate ? RunStage.Failed : RunStage.AwaitPublicationApproval, state.Stage);
        Assert.Equal(!failingCandidate, state.After!.Passed);
        Assert.Equal(3, provider.ToolResponses);
        Assert.Equal(new[] { "read_policy", "replace_expression", "verify" }, provider.ToolNames.Order(StringComparer.Ordinal));
        Assert.Contains(state.Events, item => item.Action == "replace_expression");
        Assert.Contains("Calculate compensation for eligible delay claims", provider.FirstPrompt);
        Assert.Contains("Baseline report", provider.FirstPrompt);
        Assert.DoesNotContain(Workflow.ThresholdBefore, provider.FirstPrompt);
        Assert.DoesNotContain(Workflow.ThresholdAfter, provider.FirstPrompt);
        Assert.NotNull(state.Explanation);
        Assert.NotNull(state.Usage);
        Assert.NotEmpty(state.Usage.Calls);
        Assert.Equal(provider.Requests, state.Usage.Calls.Count);
        Assert.Equal(provider.Requests * 100, state.Usage.InputTokens);
        Assert.Equal(provider.Requests * 10, state.Usage.OutputTokens);
        Assert.Equal(provider.Requests * 20, state.Usage.CacheReadTokens);
        Assert.Equal(state.Usage.InputTokens, fixture.Store.Read().Usage!.InputTokens);
        Assert.Null(state.Usage.CopilotBillingMultiplierCost);
        Assert.Equal(request.TraceId.ToHexString(), state.TraceId);
        Assert.Contains(spans, span => span.OperationName == "sdk.repair");
        foreach (var name in new[] { "sdk.read_policy", "sdk.replace_expression", "sdk.verify" })
            Assert.Contains(spans, span => span.OperationName == name);
        Assert.Single(spans.Select(span => span.TraceId).Distinct());
        var repair = Assert.Single(spans, span => span.OperationName == "sdk.repair");
        Assert.Equal(request.SpanId, repair.ParentSpanId);
        var modelCalls = spans.Where(span => span.OperationName == "chat gpt-4.1").ToList();
        Assert.Equal(provider.Requests, modelCalls.Count);
        Assert.All(modelCalls, span =>
        {
            Assert.Equal(ActivityKind.Client, span.Kind);
            Assert.Equal(repair.SpanId, span.ParentSpanId);
            Assert.Equal(state.Id, span.GetTagItem("demo.run_id"));
        });
        var allowedTags = new HashSet<string>
        {
            "demo.run_id", "gen_ai.operation.name", "gen_ai.agent.name", "gen_ai.request.model",
            "gen_ai.tool.name", "gen_ai.provider.name", "gen_ai.system", "gen_ai.response.model",
            "gen_ai.usage.input_tokens", "gen_ai.usage.output_tokens",
            "gen_ai.usage.cache_read.input_tokens", "gen_ai.usage.cache_creation.input_tokens",
            "demo.copilot.billing_multiplier_cost"
        };
        Assert.Contains(spans, span => span.OperationName == "chat gpt-4.1" &&
            Equals(span.GetTagItem("gen_ai.usage.input_tokens"), 100d));
        Assert.All(spans, span =>
        {
            Assert.All(span.TagObjects, tag => Assert.Contains(tag.Key, allowedTags));
            Assert.Empty(span.Events);
        });
    }
}

internal sealed class ScriptedProvider : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task server;
    private readonly string replacement;
    public string Url { get; }
    public int ToolResponses { get; private set; }
    public int Requests { get; private set; }
    public HashSet<string> ToolNames { get; } = [];
    public string FirstPrompt { get; private set; } = "";

    public ScriptedProvider(string? replacement = null)
    {
        this.replacement = replacement ?? Workflow.ThresholdAfter;
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var number = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        Url = $"http://127.0.0.1:{number}/v1";
        listener.Prefixes.Add($"http://127.0.0.1:{number}/");
        listener.Start();
        server = ServeAsync();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(cancellation.Token);
                Requests++;
                using var body = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: cancellation.Token);
                if (FirstPrompt.Length == 0)
                    FirstPrompt = string.Join("\n", body.RootElement.GetProperty("messages").EnumerateArray()
                        .Where(message => message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                        .Select(message => message.GetProperty("content").GetString()));
                var tools = body.RootElement.TryGetProperty("tools", out var definitions)
                    ? definitions.EnumerateArray().Select(item => item.GetProperty("function").GetProperty("name").GetString()!).ToArray()
                    : [];
                var step = tools.Length > 0 ? ToolResponses : 3;
                foreach (var tool in tools) ToolNames.Add(tool);
                var toolName = step switch { 0 => "read_policy", 1 => "replace_expression", 2 => "verify", _ => null };
                var arguments = step switch
                {
                    1 => JsonSerializer.Serialize(new { original = Workflow.ThresholdBefore, replacement }),
                    _ => "{}"
                };
                object message;
                if (toolName is not null)
                {
                    var name = tools.Single(item => item.EndsWith(toolName, StringComparison.Ordinal));
                    message = new
                    {
                        role = "assistant",
                        tool_calls = new[] { new { index = 0, id = $"call_{step}", type = "function", function = new { name, arguments } } }
                    };
                    ToolResponses++;
                }
                else message = new { role = "assistant", content = "Local scripted provider completed the test tool sequence." };
                var streaming = body.RootElement.TryGetProperty("stream", out var stream) && stream.GetBoolean();
                var response = streaming
                    ? "data: " + JsonSerializer.Serialize(new
                    {
                        id = $"local-test-{Requests}",
                        @object = "chat.completion.chunk",
                        created = 1,
                        model = "gpt-4.1",
                        choices = new[] { new { index = 0, delta = message, finish_reason = (string?)null } }
                    }) + "\n\ndata: " + JsonSerializer.Serialize(new
                    {
                        id = $"local-test-{Requests}",
                        @object = "chat.completion.chunk",
                        created = 1,
                        model = "gpt-4.1",
                        choices = new[] { new { index = 0, delta = new { }, finish_reason = toolName is null ? "stop" : "tool_calls" } },
                        usage = new { prompt_tokens = 100, completion_tokens = 10, total_tokens = 110,
                            prompt_tokens_details = new { cached_tokens = 20 } }
                    }) + "\n\ndata: [DONE]\n\n"
                    : JsonSerializer.Serialize(new
                    {
                        id = $"local-test-{Requests}",
                        @object = "chat.completion",
                        created = 1,
                        model = "gpt-4.1",
                        choices = new[] { new { index = 0, message, finish_reason = toolName is null ? "stop" : "tool_calls" } },
                        usage = new { prompt_tokens = 100, completion_tokens = 10, total_tokens = 110,
                            prompt_tokens_details = new { cached_tokens = 20 } }
                    });
                var bytes = Encoding.UTF8.GetBytes(response);
                context.Response.ContentType = streaming ? "text/event-stream" : "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, cancellation.Token);
                context.Response.Close();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        await server;
        listener.Close();
        cancellation.Dispose();
    }
}
