using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Demo.Agent;
using Xunit;

namespace Demo.Tests;

public sealed class ManagedLifecycleTests
{
    [Theory]
    [InlineData("FR-004: Rounding: down", "down")]
    [InlineData("<p>FR-004: Rounding: nearest</p><p>FR-005: retries</p>", "nearest")]
    [InlineData("FR-004: Rounding: UP. FR-005: retries", "up")]
    [InlineData("FR-004: needs a decision (down, up, nearest)", null)]
    [InlineData("FR-004: Rounding: down or up", null)]
    [InlineData("FR-004: Rounding: down\nFR-004: Rounding: up", null)]
    [InlineData("FR-004: Rounding: down\nRounding: up", null)]
    [InlineData("FR-004: Rounding: nearest (unresolved)", null)]
    [InlineData("FR-004: Rounding: down; pending approval", null)]
    public void Only_unambiguous_authoritative_FR004_is_accepted(string criteria, string? expected) =>
        Assert.Equal(expected, Workflow.ParseRounding(criteria));

    [Theory]
    [InlineData("create-story", "--approve-create")]
    [InlineData("reset", "--approve-reset")]
    [InlineData("open-bug", "--approve-create-bug")]
    [InlineData("resolve-bug", "--ack-local-candidate")]
    public async Task Remote_actions_reject_missing_or_wrong_flags_before_network(string command, string flag)
    {
        using var fixture = new DemoFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => DemoCli.ExecuteCommandAsync(fixture.Root,
            [command, "test", "--by", "presenter"], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => DemoCli.ExecuteCommandAsync(fixture.Root,
            [command, "test", "--by", "presenter", "--wrong"], CancellationToken.None));
        Assert.NotEqual("--wrong", flag);
    }

    [Fact]
    public async Task Story_question_human_refresh_baseline_bug_repair_publication_resolution_and_clean_reset()
    {
        using var fixture = new DemoFixture();
        using var fake = new MemoryAdo();
        using var http = new HttpClient(fake);
        var ado = new AdoClient(http, "demo", "Project", 0);
        var managed = new ManagedDemo(fixture.Store);
        var story = await managed.CreateStoryAsync(ado, "presenter", true, default);
        Assert.Equal("User Story", story.Type);
        Assert.Null(Workflow.ParseRounding(story.AcceptanceCriteria));
        var storyClient = ado.ForItem(story.Id);
        var state = fixture.Workflow.Init("test", story);
        Assert.Throws<InvalidOperationException>(() => fixture.Workflow.Clarify(state, "down"));
        await fixture.Workflow.AskAsync(state, storyClient, "presenter", default);
        Assert.Contains("FR-004", fake.LastHistory);
        await fixture.Workflow.CheckAsync(state, storyClient, default);
        Assert.Null(state.Rounding);
        Assert.Throws<InvalidOperationException>(() => fixture.Workflow.Approve(state, "presenter"));

        fake.Answer(story.Id, "down");
        // This is a subsequent controller instance reading persisted state, not a supplied local answer.
        var resumed = new Workflow(fixture.Store, DemoCli.DotnetPath());
        state = fixture.Store.Read();
        await resumed.CheckAsync(state, storyClient, default);
        Assert.Equal("down", state.Rounding);
        Assert.Null(state.TicketApproval);
        resumed.Approve(state, "presenter");
        fake.Answer(story.Id, "nearest");
        await resumed.CheckAsync(state, storyClient, default);
        Assert.Null(state.TicketApproval);
        Assert.Equal(RunStage.AwaitTicketApproval, state.Stage);
        resumed.Approve(state, "presenter");
        var unchangedApproval = state.TicketApproval;
        fake.Items[story.Id] = fake.Items[story.Id] with { Revision = fake.Items[story.Id].Revision + 1 };
        await resumed.CheckAsync(state, storyClient, default);
        Assert.Equal(unchangedApproval, state.TicketApproval);
        resumed.ValidateBaseline(state);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resumed.OpenBugAsync(state, storyClient, "presenter", true, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resumed.ExecuteAsync(state, new RehearsalWorker(), "rehearsal", default));
        var writes = fake.Writes;
        await resumed.TestBaselineAsync(state, default);
        Assert.Equal(writes, fake.Writes);
        Assert.False(state.Before!.Passed);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resumed.OpenBugAsync(state, storyClient, "presenter", false, default));
        await resumed.OpenBugAsync(state, storyClient, "presenter", true, default);
        var bugId = state.Bug!.Id;
        Assert.Equal(story.Id, fake.ParentByBug[bugId]);
        Assert.Equal("New", state.Bug.State);
        // ADO may advance the parent revision for the reverse hierarchy link.
        fake.Items[story.Id] = fake.Items[story.Id] with { Revision = fake.Items[story.Id].Revision + 1 };
        await resumed.CheckAsync(state, storyClient, default);
        Assert.Equal(unchangedApproval, state.TicketApproval);
        writes = fake.Writes;
        await resumed.ExecuteAsync(state, new RehearsalWorker(), "rehearsal", default);
        Assert.Equal(writes, fake.Writes);
        Assert.True(state.After!.Passed);
        resumed.Export(state, "reviewer");

        var report = Path.Combine(fixture.Store.DirectoryPath, "candidate-results.json");
        var original = File.ReadAllText(report);
        File.AppendAllText(report, " ");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resumed.PublishAsync(state, storyClient, "reviewer", default));
        Assert.Equal(writes, fake.Writes);
        File.WriteAllText(report, original);
        await resumed.PublishAsync(state, storyClient, "reviewer", default);
        Assert.Contains("No code push", fake.LastHistory);
        Assert.Equal("New", fake.Items[bugId].State);
        var findings = Assert.Single(fake.Histories[bugId]);
        Assert.Contains("Scripted rehearsal, no model inference", findings);
        Assert.Contains("-delayMinutes >= 120 ? 50 : delayMinutes > 60 ? 25 : 0", findings);
        Assert.Contains("+delayMinutes >= 120 ? 50 : delayMinutes >= 60 ? 25 : 0", findings);
        Assert.Contains("Expected 1500 rappen; actual 0.", findings);
        Assert.Contains("candidate passed 12 checks", findings);
        Assert.Contains(state.CandidateHash!, findings);
        Assert.NotNull(state.BugFindingsApproval);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resumed.ResolveBugAsync(state, storyClient, "reviewer", false, default));
        var beforeEdit = fake.Items[bugId];
        fake.Items[bugId] = beforeEdit with { Revision = beforeEdit.Revision + 1 };
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            resumed.ResolveBugAsync(state, storyClient, "reviewer", true, default));
        Assert.Null(state.ResolutionApproval);
        fake.Items[bugId] = beforeEdit;
        await resumed.ResolveBugAsync(state, storyClient, "reviewer", true, default);
        Assert.Equal("Resolved", state.Bug.State);
        Assert.Contains("Not committed, integrated, merged or deployed", fake.LastHistory);
        Assert.NotNull(state.ResolutionApproval);

        File.WriteAllText(Path.Combine(fixture.Root, "demo", "ticket.json"), "{}");
        var fresh = await managed.ResetAsync(ado, "presenter", true, default);
        Assert.NotEqual(story.Id, fresh.Id);
        Assert.Null(Workflow.ParseRounding(fresh.AcceptanceCriteria));
        Assert.Equal("New", fresh.State);
        Assert.False(File.Exists(Path.Combine(fixture.Store.DirectoryPath, "state.json")));
        Assert.False(Directory.Exists(fixture.Store.Workspace));
        Assert.Contains(story.Id, fake.Deleted);
        Assert.Contains(bugId, fake.Deleted);
        Assert.DoesNotContain(1, fake.Deleted);
        Assert.True(fake.Items.ContainsKey(1));
        Assert.Empty(managed.Read(ado).BugIds);
        Assert.Equal(story.AcceptanceCriteria, fresh.AcceptanceCriteria);
        var restarted = fixture.Workflow.Init("test", fresh);
        Assert.Null(restarted.Rounding);
        Assert.Null(restarted.TicketApproval);
    }

    [Fact]
    public async Task Story_failure_after_bug_comment_can_resume_without_duplicate_bug_findings()
    {
        using var fixture = new DemoFixture();
        using var fake = new MemoryAdo();
        using var http = new HttpClient(fake);
        var (ado, state) = await PrepareExportedAsync(fixture, http, fake);
        var storyId = state.CurrentTicket!.Id;
        fake.FailPatchId = storyId;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workflow.PublishAsync(state, ado, "reviewer", default));
        state = fixture.Store.Read();
        Assert.Equal(RunStage.Exported, state.Stage);
        Assert.Null(state.AdoApproval);
        Assert.NotNull(state.BugFindingsApproval);
        Assert.Single(fake.Histories[state.Bug!.Id]);
        fake.FailPatchId = null;
        await fixture.Workflow.PublishAsync(state, ado, "reviewer", default);
        await fixture.Workflow.PublishAsync(state, ado, "reviewer", default);
        Assert.Equal(RunStage.Published, state.Stage);
        Assert.Single(fake.Histories[state.Bug.Id]);
        Assert.Equal(2, fake.Histories[storyId].Count); // Clarification question and findings.
        Assert.Equal("New", fake.Items[state.Bug.Id].State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_story_or_bug_blocks_all_findings_writes(bool changeBug)
    {
        using var fixture = new DemoFixture();
        using var fake = new MemoryAdo();
        using var http = new HttpClient(fake);
        var (ado, state) = await PrepareExportedAsync(fixture, http, fake);
        var id = changeBug ? state.Bug!.Id : state.CurrentTicket!.Id;
        fake.Items[id] = fake.Items[id] with { Revision = fake.Items[id].Revision + 1 };
        var writes = fake.Writes;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workflow.PublishAsync(state, ado, "reviewer", default));
        Assert.Equal(writes, fake.Writes);
        Assert.Null(state.BugFindingsApproval);
        Assert.Null(state.AdoApproval);
    }

    [Fact]
    public async Task Lost_bug_patch_response_is_not_blindly_retried()
    {
        using var fixture = new DemoFixture();
        using var fake = new MemoryAdo();
        using var http = new HttpClient(fake);
        var (ado, state) = await PrepareExportedAsync(fixture, http, fake);
        fake.LosePatchResponseId = state.Bug!.Id;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Workflow.PublishAsync(state, ado, "reviewer", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workflow.PublishAsync(state, ado, "reviewer", default));
        Assert.Single(fake.Histories[state.Bug.Id]);
        Assert.Single(fake.Histories[state.CurrentTicket!.Id]); // Only the clarification question.
        Assert.Null(state.BugFindingsApproval);
        Assert.Equal(RunStage.Exported, state.Stage);
    }

    [Fact]
    public async Task Previously_published_story_can_receive_missing_bug_findings_without_duplicate_story_comment()
    {
        using var fixture = new DemoFixture();
        using var fake = new MemoryAdo();
        using var http = new HttpClient(fake);
        var (ado, state) = await PrepareExportedAsync(fixture, http, fake);
        state.PublishedTicket = await ado.AddHistoryAsync(state.CurrentTicket!, "Previously published Story evidence", default);
        state.Stage = RunStage.Published;
        fixture.Store.Save(state);
        var writes = fake.Writes;
        await fixture.Workflow.PublishAsync(state, ado, "reviewer", default);
        Assert.Equal(writes + 1, fake.Writes);
        Assert.Single(fake.Histories[state.Bug!.Id]);
        Assert.Equal(2, fake.Histories[state.CurrentTicket!.Id].Count);
        Assert.NotNull(state.BugFindingsApproval);
    }

    private static async Task<(AdoClient Ado, RunState State)> PrepareExportedAsync(
        DemoFixture fixture, HttpClient http, MemoryAdo fake)
    {
        var ado = new AdoClient(http, "demo", "Project", 0);
        var story = await new ManagedDemo(fixture.Store).CreateStoryAsync(ado, "presenter", true, default);
        ado = ado.ForItem(story.Id);
        var state = fixture.Workflow.Init("test", story);
        await fixture.Workflow.AskAsync(state, ado, "presenter", default);
        fake.Answer(story.Id, "nearest");
        await fixture.Workflow.CheckAsync(state, ado, default);
        fixture.Workflow.Approve(state, "presenter");
        await fixture.Workflow.TestBaselineAsync(state, default);
        await fixture.Workflow.OpenBugAsync(state, ado, "presenter", true, default);
        await fixture.Workflow.ExecuteAsync(state, new RehearsalWorker(), "rehearsal", default);
        fixture.Workflow.Export(state, "reviewer");
        return (ado, state);
    }

    [Fact]
    public async Task Managed_writes_require_action_approval_and_scope_and_ownership()
    {
        using var fixture = new DemoFixture();
        using var fake = new MemoryAdo();
        using var http = new HttpClient(fake);
        var ado = new AdoClient(http, "demo", "Project", 0);
        var managed = new ManagedDemo(fixture.Store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => managed.CreateStoryAsync(ado, "presenter", false, default));
        await Assert.ThrowsAsync<ArgumentException>(() => managed.CreateStoryAsync(ado, "", true, default));
        Assert.Equal(0, fake.Requests);
        var story = await managed.CreateStoryAsync(ado, "presenter", true, default);
        Assert.Throws<InvalidOperationException>(() => managed.Read(new AdoClient(http, "other", "Project", 0)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => managed.ResetAsync(ado, "presenter", false, default));
        fake.Items[story.Id] = story with { Tags = "" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => managed.ResetAsync(ado, "presenter", true, default));
        Assert.Empty(fake.Deleted);
        Assert.True(managed.Read(ado).Resetting);
    }

    [Fact]
    public async Task Ambiguous_create_and_delete_are_reconciled_without_duplicate_writes()
    {
        using var fixture = new DemoFixture();
        using var fake = new MemoryAdo { LoseCreateResponse = true };
        using var http = new HttpClient(fake);
        var ado = new AdoClient(http, "demo", "Project", 0);
        var managed = new ManagedDemo(fixture.Store);
        await Assert.ThrowsAsync<HttpRequestException>(() => managed.CreateStoryAsync(ado, "presenter", true, default));
        fake.HideQueryResults = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => managed.CreateStoryAsync(ado, "presenter", true, default));
        Assert.Equal(1, fake.Creates);
        fake.HideQueryResults = false;
        var story = await managed.CreateStoryAsync(ado, "presenter", true, default);
        Assert.Equal(1, fake.Creates);
        fake.LoseDeleteResponse = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => managed.ResetAsync(ado, "presenter", true, default));
        Assert.True(managed.Read(ado).Resetting);
        var fresh = await managed.ResetAsync(ado, "presenter", true, default);
        Assert.NotEqual(story.Id, fresh.Id);
        Assert.Equal(2, fake.Creates);
        Assert.Single(fake.Deleted);
        Assert.DoesNotContain(1, fake.Deleted);
    }

    [Fact]
    public async Task Rejected_recreation_retains_new_generation_and_retries_setup_without_redeleting()
    {
        using var fixture = new DemoFixture();
        using var fake = new MemoryAdo();
        using var http = new HttpClient(fake);
        var ado = new AdoClient(http, "demo", "Project", 0);
        var managed = new ManagedDemo(fixture.Store);
        var story = await managed.CreateStoryAsync(ado, "presenter", true, default);
        fake.RejectCreate = true;
        await Assert.ThrowsAsync<AdoCreateRejectedException>(() => managed.ResetAsync(ado, "presenter", true, default));
        Assert.Null(managed.Read(ado).StoryId);
        Assert.Null(managed.Read(ado).PendingCreate);
        Assert.False(managed.Read(ado).Resetting);
        fake.RejectCreate = false;
        var fresh = await managed.CreateStoryAsync(ado, "presenter", true, default);
        Assert.NotEqual(story.Id, fresh.Id);
        Assert.Single(fake.Deleted);
        Assert.Equal(2, fake.Creates);
    }

    private sealed class MemoryAdo : HttpMessageHandler
    {
        public Dictionary<int, WorkItem> Items { get; } = new()
        {
            [1] = new(1, 1, "https://dev.azure.com/demo/Project/_workitems/edit/1", "Unrelated", "Do not touch", "", "Bug", "New", "", "Project")
        };
        private readonly Dictionary<int, WorkItem> recycled = [];
        public Dictionary<int, int> ParentByBug { get; } = [];
        public List<int> Deleted { get; } = [];
        public bool LoseCreateResponse { get; set; }
        public bool LoseDeleteResponse { get; set; }
        public bool HideQueryResults { get; set; }
        public bool RejectCreate { get; set; }
        public int Creates { get; private set; }
        public int Writes { get; private set; }
        public int Requests { get; private set; }
        public string LastHistory { get; private set; } = "";
        public Dictionary<int, List<string>> Histories { get; } = [];
        public int? FailPatchId { get; set; }
        public int? LosePatchResponseId { get; set; }
        private int nextId = 10;

        public void Answer(int id, string rounding)
        {
            var item = Items[id];
            var criteria = System.Text.RegularExpressions.Regex.Replace(item.AcceptanceCriteria,
                @"FR-004:.*?(?=FR-005:|$)", $"FR-004: Rounding: {rounding}. ");
            Items[id] = item with { Revision = item.Revision + 1, AcceptanceCriteria = criteria };
        }

        private static HttpResponseMessage Response(WorkItem item) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                id = item.Id,
                rev = item.Revision,
                fields = Fields(item)
            })
        };

        private static Dictionary<string, string?> Fields(WorkItem item) => new()
        {
            ["System.Title"] = item.Title,
            ["System.Description"] = item.Description,
            ["Microsoft.VSTS.Common.AcceptanceCriteria"] = item.AcceptanceCriteria,
            ["System.WorkItemType"] = item.Type,
            ["System.State"] = item.State,
            ["System.Tags"] = item.Tags,
            ["System.TeamProject"] = item.Project
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            Assert.Equal("dev.azure.com", request.RequestUri!.Host);
            var path = Uri.UnescapeDataString(request.RequestUri.AbsolutePath);
            if (path.EndsWith("/wiql", StringComparison.Ordinal))
            {
                using var query = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var text = query.RootElement.GetProperty("query").GetString()!;
                var matches = Items.Values.Where(item => !HideQueryResults && item.Tags is { Length: > 0 } && text.Contains(item.Tags, StringComparison.Ordinal));
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { workItems = matches.Select(item => new { id = item.Id }) }) };
            }
            if (request.Method == HttpMethod.Post)
            {
                if (RejectCreate) return new(HttpStatusCode.BadRequest);
                using var patch = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                string Field(string field) => patch.RootElement.EnumerateArray().FirstOrDefault(op => op.GetProperty("path").GetString() == "/fields/" + field)
                    is var element && element.ValueKind != JsonValueKind.Undefined ? element.GetProperty("value").GetString()! : "";
                var type = path.EndsWith("$User Story", StringComparison.Ordinal) ? "User Story" : "Bug";
                var id = nextId++;
                var item = new WorkItem(id, 1, $"https://dev.azure.com/demo/Project/_workitems/edit/{id}",
                    Field("System.Title"), Field("System.Description"), Field("Microsoft.VSTS.Common.AcceptanceCriteria"),
                    type, "New", Field("System.Tags"), "Project");
                if (type == "Bug")
                {
                    var link = patch.RootElement.EnumerateArray().Single(op => op.GetProperty("path").GetString() == "/relations/-").GetProperty("value");
                    Assert.Equal("System.LinkTypes.Hierarchy-Reverse", link.GetProperty("rel").GetString());
                    ParentByBug[id] = int.Parse(link.GetProperty("url").GetString()!.Split('/').Last());
                }
                Items[id] = item;
                Writes++;
                Creates++;
                if (LoseCreateResponse)
                {
                    LoseCreateResponse = false;
                    throw new HttpRequestException("Synthetic lost create response.");
                }
                return Response(item);
            }
            var itemId = int.Parse(path.Split('/').Last());
            if (path.Contains("/recyclebin/", StringComparison.Ordinal))
            {
                if (!recycled.TryGetValue(itemId, out var removed)) return new(HttpStatusCode.NotFound);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { resource = new { id = removed.Id, rev = removed.Revision, fields = Fields(removed) } }) };
            }
            if (!Items.TryGetValue(itemId, out var current)) return new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Delete)
            {
                Assert.False(request.RequestUri.Query.Contains("destroy", StringComparison.OrdinalIgnoreCase));
                recycled[itemId] = current;
                Items.Remove(itemId);
                Deleted.Add(itemId);
                Writes++;
                if (LoseDeleteResponse)
                {
                    LoseDeleteResponse = false;
                    throw new HttpRequestException("Synthetic lost delete response.");
                }
                return new(HttpStatusCode.OK);
            }
            if (request.Method == HttpMethod.Patch)
            {
                if (FailPatchId == itemId) return new(HttpStatusCode.BadRequest);
                using var patch = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("/rev", patch.RootElement[0].GetProperty("path").GetString());
                if (patch.RootElement[0].GetProperty("value").GetInt32() != current.Revision)
                    return new(HttpStatusCode.PreconditionFailed);
                foreach (var op in patch.RootElement.EnumerateArray().Skip(1))
                {
                    if (op.GetProperty("path").GetString() == "/fields/System.State")
                        current = current with { State = op.GetProperty("value").GetString() };
                    else
                    {
                        LastHistory = WebUtility.HtmlDecode(op.GetProperty("value").GetString()!);
                        if (!Histories.TryGetValue(itemId, out var histories))
                            Histories[itemId] = histories = [];
                        histories.Add(LastHistory);
                    }
                }
                current = current with { Revision = current.Revision + 1 };
                Items[itemId] = current;
                Writes++;
                if (LosePatchResponseId == itemId)
                {
                    LosePatchResponseId = null;
                    throw new HttpRequestException("Synthetic lost patch response.");
                }
            }
            return Response(current);
        }
    }
}
