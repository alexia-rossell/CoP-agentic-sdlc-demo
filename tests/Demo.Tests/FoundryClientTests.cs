using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Demo.Agent;
using Xunit;

namespace Demo.Tests;

public sealed class FoundryClientTests
{
    private static readonly FoundryDeployment Deployment = new("subscription",
        "https://demo.services.ai.azure.com/api/projects/demo/agents/demo", "session/one");
    private const string Approval = "synthetic-presenter-secret-32-characters";

    [Theory]
    [InlineData("status", 1)]
    [InlineData("init-offline", 1)]
    [InlineData("check", 2)]
    [InlineData("ask", 2)]
    public async Task Client_preserves_commands_session_and_scoped_credentials(string command, int expectedTokens)
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var resources = new List<string>();
        var result = await FoundryClient.InvokeAsync(Deployment, Approval, [command, "live1"],
            (resource, subscription, _) =>
            {
                Assert.Equal("subscription", subscription);
                resources.Add(resource);
                return Task.FromResult("synthetic-token-" + resources.Count);
            }, http, CancellationToken.None);
        Assert.Equal(expectedTokens, resources.Count);
        Assert.Equal("https://ai.azure.com", resources[0]);
        Assert.Contains("agent_session_id=session%2Fone", handler.Url);
        using var body = JsonDocument.Parse(handler.Body!);
        var forwarded = body.RootElement.GetProperty("args").Deserialize<string[]>();
        Assert.NotNull(forwarded);
        Assert.Equal([command, "live1"], forwarded);
        Assert.Equal(Approval, handler.Headers!["X-Client-Demo-Approval"].Single());
        Assert.Equal(command is "check" or "ask", handler.Headers.ContainsKey("X-Client-Demo-Ado-Token"));
        Assert.DoesNotContain("synthetic-token", handler.Body);
        Assert.Equal("Draft", result.GetProperty("state").GetProperty("stage").GetString());
    }

    [Fact]
    public async Task Sdk_execution_gets_fresh_model_tokens_on_every_invocation()
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var resources = new List<string>();
        for (var invocation = 0; invocation < 2; invocation++)
            await FoundryClient.InvokeAsync(Deployment, Approval, ["execute", "live1", "--mode", "sdk"],
                (resource, _, _) =>
                {
                    resources.Add(resource);
                    return Task.FromResult("synthetic-token");
                }, http, CancellationToken.None);
        Assert.Equal(6, resources.Count);
        Assert.Equal(2, resources.Count(resource => resource == "https://cognitiveservices.azure.com"));
        Assert.True(handler.Headers!.ContainsKey("X-Client-Demo-Model-Token"));
    }

    [Theory]
    [InlineData("http://demo.services.ai.azure.com")]
    [InlineData("https://example.com")]
    [InlineData("https://demo.services.ai.azure.com:8443")]
    [InlineData("https://demo.services.ai.azure.com?query=1")]
    [InlineData("https://demo.services.ai.azure.com/#fragment")]
    public async Task Invalid_endpoints_never_receive_credentials(string endpoint)
    {
        using var http = new HttpClient(new RecordingHandler());
        await Assert.ThrowsAsync<InvalidDataException>(() => FoundryClient.InvokeAsync(
            Deployment with { Endpoint = endpoint }, Approval, ["status", "live1"],
            (_, _, _) => throw new InvalidOperationException("Must not request credentials."),
            http, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_session_and_short_secret_fail_before_authentication()
    {
        using var http = new HttpClient(new RecordingHandler());
        foreach (var (deployment, secret) in new[] { (Deployment with { Session = "" }, Approval), (Deployment, "short") })
            await Assert.ThrowsAsync<InvalidDataException>(() => FoundryClient.InvokeAsync(
                deployment, secret, ["status", "live1"],
                (_, _, _) => throw new InvalidOperationException("Must not request credentials."),
                http, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Redirects_and_failures_are_errors_without_retries(HttpStatusCode status)
    {
        using var handler = new RecordingHandler { Status = status };
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => FoundryClient.InvokeAsync(
            Deployment, Approval, ["ask", "live1", "--by", "presenter"],
            (_, _, _) => Task.FromResult("synthetic-token"), http, CancellationToken.None));
        Assert.Equal(1, handler.Requests);
        Assert.Contains(status == HttpStatusCode.Found ? "redirect" : "Do not blindly retry", error.Message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public int Requests { get; private set; }
        public string? Url { get; private set; }
        public string? Body { get; private set; }
        public Dictionary<string, IEnumerable<string>>? Headers { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            Url = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(token);
            Headers = request.Headers.ToDictionary(header => header.Key, header => header.Value);
            return new(Status) { Content = JsonContent.Create(new { state = new { stage = "Draft" } }) };
        }
    }
}
