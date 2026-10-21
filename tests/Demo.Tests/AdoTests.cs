using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Demo.Agent;
using Xunit;

namespace Demo.Tests;

public sealed class AdoTests
{
    [Fact]
    public void Missing_live_configuration_never_falls_back_to_a_fixture()
    {
        using var http = new HttpClient();
        Assert.Throws<InvalidOperationException>(() => AdoClient.FromEnvironment(http, _ => null));
    }

    [Theory]
    [InlineData("https://dev.azure.com/demo", "12")]
    [InlineData("demo", "0")]
    [InlineData("demo", "not-an-id")]
    public void Live_configuration_validates_the_destination(string organization, string id)
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => AdoClient.FromEnvironment(http, key => key switch
        {
            "ADO_ORGANIZATION" => organization,
            "ADO_WORK_ITEM_ID" => id,
            _ => "test"
        }));
    }

    [Fact]
    public async Task Live_intake_reads_all_ticket_fields_and_posts_revision_guarded_history()
    {
        using var handler = new FakeAdo();
        using var http = new HttpClient(handler);
        var ado = AdoClient.FromEnvironment(http, key => key switch
        {
            "ADO_ORGANIZATION" => "demo",
            "ADO_PROJECT" => "Demo Project",
            "ADO_WORK_ITEM_ID" => "42",
            "ADO_PAT" => "synthetic-test-pat",
            _ => null
        });
        var ticket = await ado.ReadAsync(CancellationToken.None);
        Assert.Equal(42, ticket.Id);
        Assert.Equal(3, ticket.Revision);
        Assert.Equal("Investigate compensation", ticket.Title);
        Assert.Contains("Rounding", ticket.AcceptanceCriteria);
        Assert.Equal("Basic", http.DefaultRequestHeaders.Authorization!.Scheme);
        var updated = await ado.AddHistoryAsync(ticket, "Approved by <reviewer>", CancellationToken.None);
        Assert.Equal(4, updated.Revision);
        Assert.Equal(1, handler.Writes);
        Assert.Equal(3, handler.TestedRevision);
        Assert.Equal("Approved by &lt;reviewer&gt;", handler.History);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ado.AddHistoryAsync(ticket, "Do not duplicate", CancellationToken.None));
        Assert.Equal(1, handler.Writes);
    }

    [Fact]
    public async Task Different_ticket_settings_cannot_receive_a_run_result()
    {
        using var handler = new FakeAdo();
        using var http = new HttpClient(handler);
        var ado = new AdoClient(http, "demo", "Demo Project", 42);
        var ticket = await ado.ReadAsync(CancellationToken.None);
        var other = new AdoClient(http, "different", "Demo Project", 42);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            other.AddHistoryAsync(ticket, "Wrong organization", CancellationToken.None));
        Assert.Equal(0, handler.Writes);
    }

    [Fact]
    public async Task Missing_acceptance_criteria_is_an_error()
    {
        using var handler = new FakeAdo { IncludeAcceptance = false };
        using var http = new HttpClient(handler);
        var ado = new AdoClient(http, "demo", "Demo Project", 42);
        await Assert.ThrowsAsync<InvalidDataException>(() => ado.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Ado_read_failure_is_not_a_success_shaped_default()
    {
        using var handler = new FakeAdo { ReadStatus = HttpStatusCode.Unauthorized };
        using var http = new HttpClient(handler);
        var ado = new AdoClient(http, "demo", "Demo Project", 42);
        await Assert.ThrowsAsync<HttpRequestException>(() => ado.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Question_and_publication_have_separate_approval_and_revision_gates()
    {
        using var fixture = new DemoFixture();
        using var handler = new FakeAdo();
        using var http = new HttpClient(handler);
        var ado = new AdoClient(http, "demo", "Demo Project", 42);
        var state = fixture.Workflow.Init("test", await ado.ReadAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Workflow.AskAsync(state, ado, " ", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Workflow.PublishAsync(state, ado, "reviewer", CancellationToken.None));
        Assert.Equal(0, handler.Writes);
        await fixture.Workflow.AskAsync(state, ado, "presenter", CancellationToken.None);
        Assert.Contains("250.75", handler.History);
        Assert.Equal(4, fixture.Workflow.ReadTicket().Revision);
        handler.Criteria = "FR-004: Rounding: nearest";
        handler.Revision++;
        await fixture.Workflow.CheckAsync(state, ado, CancellationToken.None);
        fixture.Workflow.Approve(state, "presenter");
        await fixture.Workflow.TestBaselineAsync(state, CancellationToken.None);
        state.Bug = new(43, 1, null, "Synthetic linked Bug", "", "", "Bug");
        state.BugApproval = new("presenter", DateTimeOffset.UtcNow, state.BaselineEvidenceHash!);
        await fixture.Workflow.ExecuteAsync(state, new RehearsalWorker(), "rehearsal", CancellationToken.None);
        fixture.Workflow.Export(state, "reviewer");

        handler.Revision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Workflow.PublishAsync(state, ado, "reviewer", CancellationToken.None));
        Assert.Equal(RunStage.Exported, state.Stage);
        Assert.Null(state.AdoApproval);
        Assert.Equal(1, handler.Writes);
    }

    [Fact]
    public async Task Verified_publication_posts_actual_mode_results_and_answer_once()
    {
        using var fixture = new DemoFixture();
        using var handler = new FakeAdo();
        using var http = new HttpClient(handler);
        var ado = new AdoClient(http, "demo", "Demo Project", 42);
        var state = fixture.Workflow.Init("test", await ado.ReadAsync(CancellationToken.None));
        handler.Criteria = "FR-004: Rounding: down";
        handler.Revision++;
        await fixture.Workflow.CheckAsync(state, ado, CancellationToken.None);
        fixture.Workflow.Approve(state, "presenter");
        await fixture.Workflow.TestBaselineAsync(state, CancellationToken.None);
        state.Bug = new(43, 1, null, "Synthetic linked Bug", "", "", "Bug");
        state.BugApproval = new("presenter", DateTimeOffset.UtcNow, state.BaselineEvidenceHash!);
        await fixture.Workflow.ExecuteAsync(state, new RehearsalWorker(), "rehearsal", CancellationToken.None);
        fixture.Workflow.Export(state, "reviewer");
        await fixture.Workflow.PublishAsync(state, ado, "reviewer", CancellationToken.None);
        Assert.Equal(RunStage.Published, fixture.Store.Read().Stage);
        Assert.NotNull(state.AdoApproval);
        Assert.Contains("rehearsal", handler.History);
        Assert.Contains("down", handler.History);
        Assert.Contains("12 checks", handler.History);
        Assert.Contains(state.CandidateHash!, handler.History);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Workflow.PublishAsync(state, ado, "reviewer", CancellationToken.None));
        Assert.Equal(1, handler.Writes);
    }

    private sealed class FakeAdo : HttpMessageHandler
    {
        public int Revision { get; set; } = 3;
        public int Writes { get; private set; }
        public int TestedRevision { get; private set; }
        public string History { get; private set; } = "";
        public bool IncludeAcceptance { get; init; } = true;
        public HttpStatusCode ReadStatus { get; init; } = HttpStatusCode.OK;
        public string Criteria { get; set; } = "Rounding needs clarification; preserve the compensation contract.";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://dev.azure.com/demo/Demo%20Project/_apis/wit/workitems/42?api-version=7.1",
                request.RequestUri!.AbsoluteUri);
            if (request.Method == HttpMethod.Get && ReadStatus != HttpStatusCode.OK)
                return new(ReadStatus);
            if (request.Method == HttpMethod.Patch)
            {
                Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);
                using var patch = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                var operations = patch.RootElement;
                Assert.Equal(2, operations.GetArrayLength());
                Assert.Equal("test", operations[0].GetProperty("op").GetString());
                Assert.Equal("/rev", operations[0].GetProperty("path").GetString());
                TestedRevision = operations[0].GetProperty("value").GetInt32();
                if (TestedRevision != Revision) return new(HttpStatusCode.PreconditionFailed);
                Assert.Equal("add", operations[1].GetProperty("op").GetString());
                Assert.Equal("/fields/System.History", operations[1].GetProperty("path").GetString());
                History = operations[1].GetProperty("value").GetString()!;
                Writes++;
                Revision++;
            }
            var fields = new Dictionary<string, string>
            {
                ["System.Title"] = "Investigate compensation",
                ["System.Description"] = "Synthetic delay-claim defect.",
                ["System.WorkItemType"] = "User Story"
            };
            if (IncludeAcceptance) fields["Microsoft.VSTS.Common.AcceptanceCriteria"] = Criteria;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = 42, rev = Revision, fields }) };
        }
    }
}
