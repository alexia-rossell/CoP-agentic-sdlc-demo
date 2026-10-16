using System.Text.Json;
using Demo.Agent;

return await DemoCli.RunAsync(args);

public static class DemoCli
{
    private const string Help = """
        SDD / Copilot SDK demo (.NET only)
        Run commands from the repository root. Each run has its own workspace.

        dotnet run --project agent/Demo.Agent -- init <run-id>
        dotnet run --project agent/Demo.Agent -- clarify <run-id> --rounding down|up|nearest
        dotnet run --project agent/Demo.Agent -- approve <run-id> --by <presenter>
        dotnet run --project agent/Demo.Agent -- execute <run-id> --mode sdk|rehearsal
        dotnet run --project agent/Demo.Agent -- status <run-id>
        dotnet run --project agent/Demo.Agent -- export <run-id> --by <presenter>

        sdk: real model/tool loop; requires DEMO_MODEL and Copilot login or BYOK.
        rehearsal: labelled deterministic tool selection; real compilation/tests.
        export: local evidence only, not an ADO PR or deployment.
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
            if (args.Length < 2) throw new ArgumentException("A command and run ID are required. Use --help.");
            var command = args[0];
            var expectedOption = command switch
            {
                "init" or "status" => null,
                "clarify" => "--rounding",
                "approve" or "export" => "--by",
                "execute" => "--mode",
                _ => throw new ArgumentException($"Unknown command '{command}'. Use --help.")
            };
            if ((expectedOption is null && args.Length != 2) ||
                (expectedOption is not null && (args.Length != 4 || args[2] != expectedOption)))
                throw new ArgumentException("Invalid arguments. Use --help; no unknown options are ignored.");
            var root = FindRoot(Environment.CurrentDirectory);
            var store = new RunStore(root, args[1]);
            using var runLock = store.Lock();
            var workflow = new Workflow(store, DotnetPath());
            if (command == "init") { workflow.Init(args[1]); return 0; }
            var state = store.Read();
            switch (command)
            {
                case "clarify": workflow.Clarify(state, args[3]); break;
                case "approve": workflow.Approve(state, args[3]); break;
                case "execute":
                    var mode = args[3];
                    IWorker worker = mode switch
                    {
                        "rehearsal" => new RehearsalWorker(),
                        "sdk" => new SdkWorker(ModelSettings.FromEnvironment()),
                        _ => throw new ArgumentException("Mode must be sdk or rehearsal.")
                    };
                    await workflow.ExecuteAsync(state, worker, mode, cancellation.Token);
                    break;
                case "export": workflow.Export(state, args[3]); break;
                case "status":
                    Console.WriteLine(JsonSerializer.Serialize(state, RunStore.Json));
                    break;
            }
            Console.WriteLine($"Run: {state.Id}; stage: {state.Stage}");
            Console.WriteLine($"Artifacts: {store.DirectoryPath}");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"ERROR: {error.Message}");
            return 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static string FindRoot(string directory)
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
