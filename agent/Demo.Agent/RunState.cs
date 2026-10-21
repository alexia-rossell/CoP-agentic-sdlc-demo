using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Demo.Agent;

public enum RunStage { Draft, AwaitTicketApproval, Approved, BaselineFailed, Executing, AwaitPublicationApproval, Exported, Published, Failed }
public sealed record Approval(string By, DateTimeOffset At, string Hash);
public sealed record RunEvent(DateTimeOffset At, string Stage, string Action, string Outcome);
public sealed record CheckResult(string Requirement, string Name, bool Passed, string? Error);
public sealed record Verification(bool Passed, string Rounding, List<CheckResult> Results);
public sealed record RunState
{
    public required string Id { get; init; }
    public RunStage Stage { get; set; } = RunStage.Draft;
    public string? Rounding { get; set; }
    public string? Mode { get; set; }
    public Approval? TicketApproval { get; set; }
    public Approval? AdoApproval { get; set; }
    public Approval? PublicationApproval { get; set; }
    public string? CandidateHash { get; set; }
    public string? EvidenceHash { get; set; }
    public WorkItem? Bug { get; set; }
    public Approval? BugApproval { get; set; }
    public Approval? BugFindingsApproval { get; set; }
    public Approval? ResolutionApproval { get; set; }
    public WorkItem? PublishedTicket { get; set; }
    public WorkItem? CurrentTicket { get; set; }
    public string? BaselineEvidenceHash { get; set; }
    public Verification? Before { get; set; }
    public Verification? After { get; set; }
    public string? Error { get; set; }
    public string? Explanation { get; set; }
    public SdkUsage? Usage { get; set; }
    public string? TraceId { get; set; }
    public List<RunEvent> Events { get; init; } = [];
}

public sealed partial class RunStore
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Root { get; }
    public string DirectoryPath { get; }
    public string Workspace => Path.Combine(DirectoryPath, "workspace");
    public string PolicyPath => Path.Combine(Workspace, "app", "Claims.Service", "CompensationPolicy.cs");
    public string Artifacts => Path.Combine(DirectoryPath, "artifacts");
    private string StatePath => Path.Combine(DirectoryPath, "state.json");

    public RunStore(string root, string id)
    {
        if (!RunIdPattern().IsMatch(id)) throw new ArgumentException("Run ID must be 1-40 lowercase letters, digits or hyphens, starting with a letter or digit.");
        Root = Path.GetFullPath(root);
        DirectoryPath = Path.Combine(Root, ".demo", "runs", id);
        EnsureLocalPath(DirectoryPath);
    }

    public IDisposable Lock()
    {
        EnsureLocalPath(DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, ".lock");
        EnsureLocalPath(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public void EnsureLocalPath(string path)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var full = Path.GetFullPath(path);
        if (full != DirectoryPath && !full.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Path is outside this run.");
        if ((File.Exists(full) || Directory.Exists(full)) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException($"Symbolic links are not allowed in run paths: {full}");
        while (current is not null && current.FullName != Root)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException($"Symbolic links are not allowed in run paths: {current.FullName}");
            current = current.Parent;
        }
    }

    public RunState Read()
    {
        EnsureLocalPath(StatePath);
        return JsonSerializer.Deserialize<RunState>(File.ReadAllText(StatePath), Json)
            ?? throw new InvalidDataException("Run state is empty.");
    }

    public void Save(RunState state)
    {
        EnsureLocalPath(StatePath);
        var temp = StatePath + ".tmp";
        EnsureLocalPath(temp);
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, StatePath, overwrite: true);
    }

    public void Record(RunState state, string action, string outcome)
    {
        state.Events.Add(new(DateTimeOffset.UtcNow, state.Stage.ToString(), action, outcome));
        Save(state);
        Console.WriteLine($"[{state.Stage}] {action}: {outcome}");
    }

    public string HashFile(string path)
    {
        EnsureLocalPath(path);
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    public string BaselineHash()
    {
        var paths = Directory.EnumerateFiles(Artifacts, "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Workspace, "*", SearchOption.AllDirectories)
                .Where(path => path != PolicyPath &&
                    !Path.GetRelativePath(Workspace, path).Split(Path.DirectorySeparatorChar)
                        .Any(segment => segment is "bin" or "obj")));
        var entries = paths.Order(StringComparer.Ordinal)
            .Select(path => $"{Path.GetRelativePath(DirectoryPath, path)}:{HashFile(path)}");
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", entries))));
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdPattern();
}
