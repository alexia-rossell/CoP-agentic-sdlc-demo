using Demo.Agent;
using Xunit;

namespace Demo.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public async Task Acceptance_runner_uses_a_writable_per_run_cli_home()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved();

        await fixture.Workflow.TestBaselineAsync(state, CancellationToken.None);

        Assert.Equal(RunStage.BaselineFailed, state.Stage);
        Assert.NotNull(state.Before);
        Assert.Equal(12, state.Before.Results.Count);
        var cliHome = Path.Combine(fixture.Store.DirectoryPath, "runtime", "dotnet-home");
        Assert.True(Directory.Exists(Path.Combine(cliHome, ".dotnet")));
        fixture.Workflow.ValidateBaseline(state);
    }

    [Fact]
    public async Task Compiler_failure_returns_actual_diagnostics_not_an_inaccessible_log_pointer()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Workflow.Init("test");
        File.AppendAllText(Path.Combine(fixture.Store.Workspace, "app/Claims.Service/ClaimLedger.cs"),
            "\n#error DEMO_RUNNER_STARTUP_FAILURE\n");
        fixture.Workflow.Clarify(state, "down");
        fixture.Workflow.Approve(state, "test-presenter");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Workflow.TestBaselineAsync(state, CancellationToken.None));

        Assert.Contains("build/startup failure", error.Message);
        Assert.Contains("DEMO_RUNNER_STARTUP_FAILURE", error.Message);
        Assert.Contains("CS1029", error.Message);
        Assert.Null(state.Before);
        Assert.NotEqual(RunStage.BaselineFailed, state.Stage);
        Assert.True(File.Exists(Path.Combine(fixture.Store.DirectoryPath, "baseline-output.log")));
    }

    [Fact]
    public void Clarification_changes_only_run_snapshot()
    {
        using var fixture = new DemoFixture();
        var original = File.ReadAllText(Path.Combine(fixture.Root, "demo/ticket.json"));
        var state = fixture.Workflow.Init("test");
        fixture.Workflow.Clarify(state, "up");
        Assert.Equal(RunStage.AwaitTicketApproval, fixture.Store.Read().Stage);
        Assert.Contains("\"rounding\": \"up\"", File.ReadAllText(Path.Combine(fixture.Store.Artifacts, "decision.json")));
        Assert.Equal(original, File.ReadAllText(Path.Combine(fixture.Root, "demo/ticket.json")));
        Assert.Null(state.TicketApproval);
    }

    [Fact]
    public void Approval_cannot_precede_clarification()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Workflow.Init("test");
        Assert.Throws<InvalidOperationException>(() => fixture.Workflow.Approve(state, "presenter"));
        Assert.Throws<ArgumentException>(() => fixture.Workflow.Clarify(state, "invented"));
    }

    [Fact]
    public void Changed_test_or_new_source_invalidates_approval()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved();
        File.AppendAllText(Path.Combine(fixture.Store.Workspace, "app/Claims.Acceptance/Program.cs"), "\n// changed\n");
        Assert.Throws<InvalidOperationException>(() => fixture.Workflow.ValidateBaseline(state));
    }

    [Fact]
    public void Added_source_invalidates_approval()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved();
        File.WriteAllText(Path.Combine(fixture.Store.Workspace, "app/Claims.Service/Extra.cs"), "class Extra {}");
        Assert.Throws<InvalidOperationException>(() => fixture.Workflow.ValidateBaseline(state));
    }

    [Fact]
    public void Changed_runtime_rounding_cannot_override_the_approved_decision()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved();
        state.Rounding = "up";
        Assert.Throws<InvalidOperationException>(() => fixture.Workflow.ValidateBaseline(state));
    }

    [Fact]
    public void Arbitrary_candidate_changes_are_rejected_before_compilation()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved();
        File.AppendAllText(fixture.Store.PolicyPath, "\nclass Unapproved {}");
        Assert.Throws<InvalidOperationException>(() => fixture.Workflow.ValidateBaseline(state));
    }

    [Fact]
    public void Arbitrary_code_and_test_edits_are_not_tools()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved();
        var tools = new WorkspaceTools(fixture.Workflow, state, CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() => tools.ReplaceExpression("anything", "anything else"));
        tools.ReplaceExpression(Workflow.ThresholdBefore, Workflow.ThresholdAfter);
        Assert.Throws<InvalidOperationException>(() => tools.ReplaceExpression(Workflow.ThresholdBefore, Workflow.ThresholdAfter));
    }

    [Fact]
    public void Calls_are_bounded()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved();
        var tools = new WorkspaceTools(fixture.Workflow, state, CancellationToken.None);
        for (var i = 0; i < 10; i++) tools.ReadPolicy();
        Assert.Throws<InvalidOperationException>(() => tools.ReadPolicy());
    }

    [Fact]
    public void Proposed_edits_are_grammar_bounded_not_an_exact_answer_allowlist()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved();
        var tools = new WorkspaceTools(fixture.Workflow, state, CancellationToken.None);
        tools.ReplaceExpression("delayMinutes > 60", "delayMinutes >= 61");
        Assert.Contains("delayMinutes >= 61", tools.ReadPolicy());
        Assert.Throws<InvalidOperationException>(() =>
            tools.ReplaceExpression("delayMinutes >= 61", "System.Environment.Exit(0)"));
        Assert.Throws<InvalidOperationException>(() =>
            tools.ReplaceExpression("numerator / 100", "0"));
    }

    [Fact]
    public void Duplicate_initialisation_is_not_a_reset()
    {
        using var fixture = new DemoFixture();
        fixture.Workflow.Init("test");
        Assert.Throws<InvalidOperationException>(() => fixture.Workflow.Init("test"));
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("UPPER")]
    [InlineData("/tmp/run")]
    [InlineData("")]
    public void Run_ids_cannot_escape_the_store(string id) =>
        Assert.Throws<ArgumentException>(() => new RunStore(Environment.CurrentDirectory, id));

    [Fact]
    public void A_second_process_cannot_lock_the_same_run()
    {
        using var fixture = new DemoFixture();
        using var first = fixture.Store.Lock();
        Assert.Throws<IOException>(() => fixture.Store.Lock());
    }

    [Fact]
    public void Symbolic_policy_paths_are_rejected()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new DemoFixture();
        var state = fixture.Approved();
        File.Delete(fixture.Store.PolicyPath);
        File.CreateSymbolicLink(fixture.Store.PolicyPath, Path.Combine(fixture.Root, "app/Claims.Service/CompensationPolicy.cs"));
        var tools = new WorkspaceTools(fixture.Workflow, state, CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() => tools.ReadPolicy());
    }

    [Theory]
    [InlineData("down")]
    [InlineData("up")]
    [InlineData("nearest")]
    public async Task Rehearsal_really_compiles_recovers_and_exports(string rounding)
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved(rounding);
        await fixture.Workflow.ExecuteAsync(state, new RehearsalWorker(), "rehearsal", CancellationToken.None);
        Assert.Equal(RunStage.AwaitPublicationApproval, state.Stage);
        Assert.False(state.Before!.Passed);
        Assert.Contains(state.Before.Results, item => item.Requirement == "FR-002" && !item.Passed);
        Assert.True(state.After!.Passed);
        Assert.Equal(12, state.After.Results.Count);
        Assert.Null(state.PublicationApproval);

        var resumed = new Workflow(new RunStore(fixture.Root, "test"), DemoCli.DotnetPath());
        var recovered = resumed.Store.Read();
        Assert.Equal(state.CandidateHash, recovered.CandidateHash);
        resumed.Export(recovered, "reviewer");
        var approval = recovered.PublicationApproval;
        resumed.Export(recovered, "reviewer-again");
        Assert.Equal(approval, recovered.PublicationApproval);
        Assert.Equal(RunStage.Exported, resumed.Store.Read().Stage);
        Assert.True(File.Exists(Path.Combine(fixture.Store.DirectoryPath, "export", "evidence.json")));
        Assert.Contains(recovered.Events, item => item.Outcome.Contains("OFFLINE REHEARSAL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_worker_is_a_failed_run_not_a_green_fallback()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Workflow.ExecuteAsync(state, new FailingWorker(), "sdk", CancellationToken.None));
        Assert.Equal(RunStage.Failed, fixture.Store.Read().Stage);
        Assert.Null(state.After);
        Assert.Null(state.CandidateHash);
        Assert.Throws<InvalidOperationException>(() => fixture.Workflow.Export(state, "presenter"));
    }

    [Fact]
    public async Task Changed_candidate_cannot_be_exported()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved();
        await fixture.Workflow.ExecuteAsync(state, new RehearsalWorker(), "rehearsal", CancellationToken.None);
        File.AppendAllText(fixture.Store.PolicyPath, "\n// changed after verification\n");
        Assert.Throws<InvalidOperationException>(() => fixture.Workflow.Export(state, "presenter"));
    }

    [Fact]
    public async Task Already_green_baseline_cannot_be_presented_as_a_repair_even_on_retry()
    {
        using var fixture = new DemoFixture();
        var state = fixture.Approved();
        var tools = new WorkspaceTools(fixture.Workflow, state, CancellationToken.None);
        tools.ReplaceExpression(Workflow.ThresholdBefore, Workflow.ThresholdAfter);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Workflow.ExecuteAsync(state, new RehearsalWorker(), "rehearsal", CancellationToken.None));
            Assert.Contains("baseline unexpectedly passed", error.Message);
            Assert.Equal(RunStage.Failed, state.Stage);
            Assert.Null(state.After);
        }
    }

    private sealed class FailingWorker : IWorker
    {
        public Task RunAsync(WorkspaceTools tools, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Model unavailable.");
    }
}

internal sealed class DemoFixture : IDisposable
{
    public string Root { get; } = Path.Combine(DemoCli.FindRoot(Environment.CurrentDirectory), ".demo", "test-fixtures", Guid.NewGuid().ToString("N"));
    public RunStore Store { get; }
    public Workflow Workflow { get; }

    public DemoFixture()
    {
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "Demo.slnx")))
            source = source.Parent;
        if (source is null) throw new DirectoryNotFoundException("Could not find demo source for test fixtures.");
        foreach (var path in new[]
        {
            "global.json", "demo/ticket.json",
            "app/Claims.Service/Claims.Service.csproj", "app/Claims.Service/CompensationPolicy.cs",
            "app/Claims.Service/ClaimLedger.cs", "app/Claims.Acceptance/Claims.Acceptance.csproj",
            "app/Claims.Acceptance/Program.cs"
        })
        {
            var destination = Path.Combine(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(source.FullName, path), destination);
        }
        Store = new RunStore(Root, "test");
        Workflow = new Workflow(Store, DemoCli.DotnetPath());
    }

    public RunState Approved(string rounding = "down")
    {
        var state = Workflow.Init("test");
        Workflow.Clarify(state, rounding);
        Workflow.Approve(state, "test-presenter");
        return state;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
