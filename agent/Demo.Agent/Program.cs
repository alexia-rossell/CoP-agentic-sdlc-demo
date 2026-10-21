using System.Text.Json;
using Azure.AI.AgentServer.Invocations;
using Demo.Agent;

if (args is ["serve"])
{
    HostedDemo.ValidateConfiguration();
    InvocationsServer.Run<HostedDemo>();
    return 0;
}
return await DemoCli.RunAsync(args);

public static class DemoCli
{
    private const string Help = """
        ADO ticket / Copilot SDK demo (.NET only, 10 minutes)
        Run from the repository root. No remote write happens implicitly.

        create-story <run-id> --by <presenter> --approve-create
        reset <run-id> --by <presenter> --approve-reset
                                             Soft-delete tracked items, recreate NEW story ID
        init <run-id>                         Read managed/configured User Story
        init-offline <run-id>                 Explicit offline ticket fixture
        ask <run-id> --by <presenter>          Approve posting the question to ADO
        check <run-id>                        Read human FR-004 AC answer from ADO
        clarify <run-id> --rounding down|up|nearest  (offline only)
        approve <run-id> --by <presenter>      Confirm ticket/test scope and answer
        test <run-id>                         Real failing baseline, no ADO write
        open-bug <run-id> --by <presenter> --approve-create-bug
        execute <run-id> --mode sdk|rehearsal
        status <run-id>
        export <run-id> --by <reviewer>        Local evidence, not a code push
        publish <run-id> --by <reviewer>       Approve posting results to ADO
        resolve-bug <run-id> --by <reviewer> --ack-local-candidate
                                             Resolve synthetic Bug, NOT deployed code
        serve                                Foundry Invocations host on port 8088

        sdk: real model/tool loop; DEMO_MODEL and Copilot login or BYOK required.
        rehearsal: labelled deterministic repair with real compilation/tests.
        ADO: ADO_ORGANIZATION, ADO_PROJECT, ADO_PAT or ADO_TOKEN.
        Managed story IDs persist locally; ADO_WORK_ITEM_ID is only for unmanaged intake.
        No code push, PR, merge or cloud deployment is performed by these commands.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["help"])
        {
            Console.WriteLine(Help);
            return 0;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, evt) => { evt.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            var state = await ExecuteCommandAsync(FindRoot(Environment.CurrentDirectory), args, cancellation.Token);
            Console.WriteLine(JsonSerializer.Serialize(state, RunStore.Json));
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"ERROR: {error.Message}");
            return 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    public static async Task<RunState> ExecuteCommandAsync(string root, string[] args, CancellationToken cancellationToken,
        Func<string, string?>? environment = null)
    {
        if (args.Length < 2) throw new ArgumentException("A command and run ID are required. Use --help.");
        var command = args[0];
        var expectedOption = command switch
        {
            "init" or "init-offline" or "status" or "check" or "test" => null,
            "clarify" => "--rounding",
            "approve" or "export" or "ask" or "publish" or "create-story" or "reset" or "open-bug" or "resolve-bug" => "--by",
            "execute" => "--mode",
            _ => throw new ArgumentException($"Unknown command '{command}'. Use --help.")
        };
        var actionFlag = command switch
        {
            "create-story" => "--approve-create",
            "reset" => "--approve-reset",
            "open-bug" => "--approve-create-bug",
            "resolve-bug" => "--ack-local-candidate",
            _ => null
        };
        if ((expectedOption is null && args.Length != 2) ||
            (expectedOption is not null && (args.Length != (actionFlag is null ? 4 : 5) || args[2] != expectedOption)) ||
            (actionFlag is not null && args[^1] != actionFlag))
            throw new ArgumentException("Invalid arguments. Use --help; unknown options are rejected.");
        var store = new RunStore(root, args[1]);
        using var runLock = store.Lock();
        var workflow = new Workflow(store, DotnetPath());
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var managed = new ManagedDemo(store);
        AdoClient Ado()
        {
            if (!managed.Exists) return AdoClient.FromEnvironment(http, environment);
            var configured = AdoClient.FromEnvironment(http, environment, requireItem: false);
            var journal = managed.Read(configured);
            if (journal.Resetting || journal.StoryId is null)
                throw new InvalidOperationException("Managed setup/reset is incomplete; finish it before running the workflow.");
            return configured.ForItem(journal.StoryId.Value);
        }
        if (command is "create-story" or "reset")
        {
            var configured = AdoClient.FromEnvironment(http, environment, requireItem: false);
            var story = command == "create-story"
                ? await managed.CreateStoryAsync(configured, args[3], true, cancellationToken)
                : await managed.ResetAsync(configured, args[3], true, cancellationToken);
            return new RunState { Id = args[1], Events = [new(DateTimeOffset.UtcNow, "Setup", command, $"User Story #{story.Id}: {story.Url}. Rounding: {Workflow.ParseRounding(story.AcceptanceCriteria) ?? "unresolved"}. No run approval.")] };
        }
        if (command == "init")
        {
            var story = await Ado().ReadAsync(cancellationToken);
            if (story.Type != "User Story") throw new InvalidOperationException("Live intake requires an Agile User Story, not an existing Bug.");
            return workflow.Init(args[1], story);
        }
        if (command == "init-offline") return workflow.Init(args[1]);
        var state = store.Read();
        switch (command)
        {
            case "clarify": workflow.Clarify(state, args[3]); break;
            case "approve":
                if (!workflow.ReadTicket().Offline)
                    await workflow.CheckAsync(state, Ado(), cancellationToken);
                workflow.Approve(state, args[3]);
                break;
            case "execute":
                if (!workflow.ReadTicket().Offline)
                    await workflow.CheckAsync(state, Ado(), cancellationToken);
                var mode = args[3];
                IWorker worker = mode switch
                {
                    "rehearsal" => new RehearsalWorker(),
                    "sdk" => new SdkWorker(ModelSettings.FromEnvironment(environment)),
                    _ => throw new ArgumentException("Mode must be sdk or rehearsal.")
                };
                await workflow.ExecuteAsync(state, worker, mode, cancellationToken);
                break;
            case "export": workflow.Export(state, args[3]); break;
            case "ask":
                await workflow.AskAsync(state, Ado(), args[3], cancellationToken);
                break;
            case "publish":
                await workflow.CheckAsync(state, Ado(), cancellationToken);
                await workflow.PublishAsync(state, Ado(), args[3], cancellationToken);
                break;
            case "check":
                await workflow.CheckAsync(state, Ado(), cancellationToken);
                break;
            case "test":
                if (!workflow.ReadTicket().Offline)
                    await workflow.CheckAsync(state, Ado(), cancellationToken);
                await workflow.TestBaselineAsync(state, cancellationToken);
                break;
            case "open-bug":
                await workflow.OpenBugAsync(state, Ado(), args[3], true, cancellationToken);
                break;
            case "resolve-bug":
                await workflow.ResolveBugAsync(state, Ado(), args[3], true, cancellationToken);
                break;
        }
        return state;
    }

    public static string FindRoot(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Demo.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Run this command inside the demo repository.");
    }

    public static string DotnetPath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath;
        var runtimeHost = Path.GetFullPath(Path.Combine(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        return File.Exists(runtimeHost) ? runtimeHost : "dotnet";
    }
}
