using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Demo.Agent;
using GitHub.Copilot;
using Xunit;

namespace Demo.Tests;

public sealed class SdkIntegrationTests
{
    [Fact]
    public async Task Real_sdk_runtime_executes_tools_against_a_local_scripted_provider()
    {
        using var fixture = new DemoFixture();
        await using var provider = new ScriptedProvider();
        var state = fixture.Approved();
        var worker = new SdkWorker(new ModelSettings("gpt-4.1", new ProviderConfig
        {
            Type = "openai",
            BaseUrl = provider.Url,
            WireApi = "completions",
            ApiKey = "local-test-only"
        }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await fixture.Workflow.ExecuteAsync(state, worker, "sdk", timeout.Token);
        Assert.Equal(RunStage.AwaitPublicationApproval, state.Stage);
        Assert.True(state.After!.Passed);
        Assert.Equal(4, provider.ToolResponses);
        Assert.Equal(new[] { "read_policy", "replace_expression", "verify" }, provider.ToolNames.Order(StringComparer.Ordinal));
        Assert.Contains(state.Events, item => item.Action == "replace_expression");
    }
}

internal sealed class ScriptedProvider : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task server;
    public string Url { get; }
    public int ToolResponses { get; private set; }
    public HashSet<string> ToolNames { get; } = [];

    public ScriptedProvider()
    {
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
                using var body = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: cancellation.Token);
                var tools = body.RootElement.TryGetProperty("tools", out var definitions)
                    ? definitions.EnumerateArray().Select(item => item.GetProperty("function").GetProperty("name").GetString()!).ToArray()
                    : [];
                var step = tools.Length > 0 ? ToolResponses : 4;
                foreach (var tool in tools) ToolNames.Add(tool);
                var toolName = step switch { 0 => "read_policy", 1 or 2 => "replace_expression", 3 => "verify", _ => null };
                var arguments = step switch
                {
                    1 => JsonSerializer.Serialize(new { original = Workflow.ThresholdBefore, replacement = Workflow.ThresholdAfter }),
                    2 => JsonSerializer.Serialize(new { original = Workflow.RoundingBefore, replacement = Workflow.RoundingAfter }),
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
                        id = "local-test",
                        @object = "chat.completion.chunk",
                        created = 1,
                        model = "gpt-4.1",
                        choices = new[] { new { index = 0, delta = message, finish_reason = (string?)null } }
                    }) + "\n\ndata: " + JsonSerializer.Serialize(new
                    {
                        id = "local-test",
                        @object = "chat.completion.chunk",
                        created = 1,
                        model = "gpt-4.1",
                        choices = new[] { new { index = 0, delta = new { }, finish_reason = toolName is null ? "stop" : "tool_calls" } }
                    }) + "\n\ndata: [DONE]\n\n"
                    : JsonSerializer.Serialize(new
                    {
                        id = "local-test",
                        @object = "chat.completion",
                        created = 1,
                        model = "gpt-4.1",
                        choices = new[] { new { index = 0, message, finish_reason = toolName is null ? "stop" : "tool_calls" } },
                        usage = new { prompt_tokens = 1, completion_tokens = 1, total_tokens = 2 }
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
