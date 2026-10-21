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
    [InlineData(true)]
    [InlineData(false)]
    public async Task Start_composes_approved_hosted_commands_in_either_option_order(bool flagFirst)
    {
        using var handler = new PresenterHandler(
            ("create-story", "Draft"), ("init", "Draft"), ("ask", "Draft"));
        using var http = new HttpClient(handler);
        string[] args = flagFirst
            ? ["start", "live3", "--approve-create", "--by", "presenter"]
            : ["start", "live3", "--by", "presenter", "--approve-create"];
        var result = await FoundryClient.InvokeAsync(Deployment, Approval, args,
            (_, _, _) => Task.FromResult("synthetic-token"), http, CancellationToken.None);

        Assert.Equal(3, handler.Commands.Count);
        Assert.Equal(["create-story", "live3", "--by", "presenter", "--approve-create"], handler.Commands[0]);
        Assert.Equal(["init", "live3"], handler.Commands[1]);
        Assert.Equal(["ask", "live3", "--by", "presenter"], handler.Commands[2]);
        Assert.Contains("Waiting for the PO", result.GetProperty("message").GetString());
        Assert.Empty(handler.Remaining);
    }

    [Fact]
    public async Task Fix_composes_baseline_bug_sdk_and_export_without_publishing()
    {
        using var handler = new PresenterHandler(
            ("check", "AwaitTicketApproval"), ("approve", "Approved"), ("test", "BaselineFailed"),
            ("open-bug", "BaselineFailed"), ("execute", "AwaitPublicationApproval"), ("export", "Exported"));
        using var http = new HttpClient(handler);
        var resources = new List<string>();
        var result = await FoundryClient.InvokeAsync(Deployment, Approval,
            ["fix", "live3", "--by", "presenter", "--approve-create-bug"],
            (resource, _, _) => { resources.Add(resource); return Task.FromResult("synthetic-token"); },
            http, CancellationToken.None);

        Assert.Equal(["check", "approve", "test", "open-bug", "execute", "export"],
            handler.Commands.Select(command => command[0]));
        Assert.Equal(["open-bug", "live3", "--by", "presenter", "--approve-create-bug"], handler.Commands[3]);
        Assert.Equal(["execute", "live3", "--mode", "sdk"], handler.Commands[4]);
        Assert.Equal(1, resources.Count(resource => resource == "https://cognitiveservices.azure.com"));
        Assert.Equal("Exported", result.GetProperty("state").GetProperty("stage").GetString());
        Assert.Equal("Synthetic review", result.GetProperty("review").GetString());
        Assert.Equal("Synthetic candidate", result.GetProperty("candidatePolicy").GetString());
        Assert.Contains("Nothing has been published", result.GetProperty("message").GetString());
        Assert.Empty(handler.Remaining);
    }

    [Fact]
    public async Task Fix_waits_for_human_without_approval_tests_or_remote_writes()
    {
        using var handler = new PresenterHandler(("check", "Draft"));
        using var http = new HttpClient(handler);
        var result = await FoundryClient.InvokeAsync(Deployment, Approval,
            ["fix", "live3", "--approve-create-bug", "--by", "presenter"],
            (_, _, _) => Task.FromResult("synthetic-token"), http, CancellationToken.None);

        Assert.Single(handler.Commands);
        Assert.Equal("check", handler.Commands[0][0]);
        Assert.Contains("Waiting for the PO's rounding decision", result.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("start")]
    [InlineData("fix")]
    public async Task Presenter_commands_require_explicit_action_flags_before_authentication(string command)
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => FoundryClient.InvokeAsync(Deployment, Approval,
            [command, "live3", "--by", "presenter"],
            (_, _, _) => throw new InvalidOperationException("Must not authenticate."),
            http, CancellationToken.None));
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("open-bug")]
    [InlineData("execute")]
    public async Task Fix_stops_on_failure_without_retry_or_publication(string failedCommand)
    {
        using var handler = new PresenterHandler(
            ("check", "AwaitTicketApproval"), ("approve", "Approved"), ("test", "BaselineFailed"),
            ("open-bug", "BaselineFailed"), ("execute", "AwaitPublicationApproval"), ("export", "Exported"))
            { FailedCommand = failedCommand };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => FoundryClient.InvokeAsync(Deployment, Approval,
            ["fix", "live3", "--by", "presenter", "--approve-create-bug"],
            (_, _, _) => Task.FromResult("synthetic-token"), http, CancellationToken.None));
        Assert.Equal(failedCommand, handler.Commands[^1][0]);
        Assert.DoesNotContain(handler.Commands, command => command[0] is "publish" or "export");
    }

    [Fact]
    public async Task Fix_does_not_automatically_retry_failed_sdk_runs()
    {
        using var handler = new PresenterHandler(("check", "Failed"));
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => FoundryClient.InvokeAsync(
            Deployment, Approval, ["fix", "live3", "--by", "presenter", "--approve-create-bug"],
            (_, _, _) => Task.FromResult("synthetic-token"), http, CancellationToken.None));
        Assert.Contains("not automatically retried", error.Message);
        Assert.Single(handler.Commands);
    }

    [Fact]
    public async Task Fix_resumes_verified_candidate_with_export_only()
    {
        using var handler = new PresenterHandler(("check", "AwaitPublicationApproval"), ("export", "Exported"));
        using var http = new HttpClient(handler);
        await FoundryClient.InvokeAsync(Deployment, Approval,
            ["fix", "live3", "--by", "presenter", "--approve-create-bug"],
            (_, _, _) => Task.FromResult("synthetic-token"), http, CancellationToken.None);
        Assert.Equal(["check", "export"], handler.Commands.Select(command => command[0]));
    }

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

    private sealed class PresenterHandler(params (string Command, string Stage)[] steps) : HttpMessageHandler
    {
        public Queue<(string Command, string Stage)> Remaining { get; } = new(steps);
        public List<string[]> Commands { get; } = [];
        public string? FailedCommand { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Contains("agent_session_id=session%2Fone", request.RequestUri!.AbsoluteUri);
            Assert.Equal(Approval, request.Headers.GetValues("X-Client-Demo-Approval").Single());
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var args = document.RootElement.GetProperty("args").Deserialize<string[]>()!;
            Commands.Add(args);
            var step = Remaining.Dequeue();
            Assert.Equal(step.Command, args[0]);
            if (args[0] == FailedCommand)
                return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { error = "Synthetic failure" }) };
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    state = new
                    {
                        id = "live3", stage = step.Stage,
                        events = new[] { new { outcome = "Synthetic setup Story link" } },
                        before = new
                        {
                            passed = false,
                            results = new[] { new { requirement = "FR-002", name = "Synthetic boundary", passed = false, error = "Synthetic failure" } }
                        }
                    },
                    review = step.Stage == "Exported" ? "Synthetic review" : null,
                    candidatePolicy = step.Stage == "Exported" ? "Synthetic candidate" : null
                })
            };
        }
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
