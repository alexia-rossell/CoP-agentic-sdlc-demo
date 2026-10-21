using System.Text.Json;

namespace Demo.Agent;

public sealed record ManagedJournal
{
    public required string Organization { get; init; }
    public required string Project { get; init; }
    public string Generation { get; set; } = Guid.NewGuid().ToString("N");
    public int? StoryId { get; set; }
    public List<int> BugIds { get; set; } = [];
    public List<int> DeletedIds { get; set; } = [];
    public bool Resetting { get; set; }
    public string? PendingCreate { get; set; }
    public string? CreateError { get; set; }
    public WorkItem? OriginalTemplate { get; set; }
}

// The local journal is the allow-list, not a search of arbitrary project work items.
public sealed class ManagedDemo(RunStore store)
{
    private string PathName => Path.Combine(store.DirectoryPath, "managed.json");
    public bool Exists => File.Exists(PathName);
    public ManagedJournal Read(AdoClient ado)
    {
        store.EnsureLocalPath(PathName);
        var journal = JsonSerializer.Deserialize<ManagedJournal>(File.ReadAllText(PathName), RunStore.Json)
            ?? throw new InvalidDataException("Empty managed demo journal.");
        if (journal.Organization != ado.Organization || journal.Project != ado.Project)
            throw new InvalidOperationException("ADO organization/project do not match the persisted demo journal.");
        return journal;
    }

    private void Save(ManagedJournal journal)
    {
        store.EnsureLocalPath(store.DirectoryPath);
        Directory.CreateDirectory(store.DirectoryPath);
        store.EnsureLocalPath(PathName);
        store.EnsureLocalPath(PathName + ".new");
        File.WriteAllText(PathName + ".new", JsonSerializer.Serialize(journal, RunStore.Json));
        File.Move(PathName + ".new", PathName, overwrite: true);
    }

    public static string Marker(ManagedJournal journal, string kind) => $"copilot-demo-{journal.Generation}-{kind}";

    public async Task<WorkItem> CreateStoryAsync(AdoClient ado, string by, bool approved, CancellationToken token)
    {
        RequireApproval(by, approved);
        if (File.Exists(Path.Combine(store.DirectoryPath, "state.json")) && !Exists)
            throw new InvalidOperationException("An unmanaged run already exists under this ID. Use a new managed demo ID.");
        var journal = Exists ? Read(ado) : new ManagedJournal { Organization = ado.Organization, Project = ado.Project };
        if (journal.Resetting) throw new InvalidOperationException("Reset is incomplete; retry reset, not create-story.");
        if (journal.StoryId is not null)
        {
            var existing = await ado.ForItem(journal.StoryId.Value).ReadAsync(token);
            ado.VerifyOwnership(existing, Marker(journal, "story"), "User Story");
            return existing;
        }
        var template = journal.OriginalTemplate ?? JsonSerializer.Deserialize<WorkItem>(File.ReadAllText(Path.Combine(store.Root, "demo", "ticket.json")), RunStore.Json)
            ?? throw new InvalidDataException("Missing unresolved requirement fixture.");
        if (!template.Offline || Workflow.ParseRounding(template.AcceptanceCriteria) is not null)
            throw new InvalidDataException("Managed setup requires the unresolved offline requirement template, never a pre-filled rounding answer.");
        journal.OriginalTemplate = template;
        Save(journal);
        var story = await CreateTrackedAsync(journal, ado, "story", template, null, token);
        Console.WriteLine($"Created managed User Story #{story.Id}: {story.Url}. Approval by {by}; rounding remains unresolved.");
        return story;
    }

    private async Task<WorkItem> CreateTrackedAsync(ManagedJournal journal, AdoClient ado, string kind,
        WorkItem template, WorkItem? parent, CancellationToken token)
    {
        var marker = Marker(journal, kind);
        if (journal.PendingCreate is not null && journal.PendingCreate != kind)
            throw new InvalidOperationException("Another create is pending; inspect and recover that action first.");
        var recovered = await ado.FindOwnedAsync(marker, token);
        if (recovered is null && journal.PendingCreate is not null)
            throw new InvalidOperationException("Earlier create outcome is uncertain. No duplicate will be created. Inspect ADO and retain the journal; retry after the item becomes query-visible. " + journal.CreateError);
        if (recovered is null)
        {
            journal.PendingCreate = kind;
            Save(journal);
            try { recovered = await ado.CreateAsync(kind == "story" ? "User Story" : "Bug", template, marker, parent, token); }
            catch (Exception error)
            {
                if (error is AdoCreateRejectedException) journal.PendingCreate = null;
                journal.CreateError = error.Message;
                Save(journal);
                throw;
            }
        }
        ado.VerifyOwnership(recovered, marker, kind == "story" ? "User Story" : "Bug");
        if (kind == "story") journal.StoryId = recovered.Id;
        else if (!journal.BugIds.Contains(recovered.Id)) journal.BugIds.Add(recovered.Id);
        journal.PendingCreate = null;
        journal.CreateError = null;
        Save(journal);
        return recovered;
    }

    public async Task<WorkItem> OpenBugAsync(AdoClient ado, WorkItem story, RunState state,
        string by, bool approved, CancellationToken token)
    {
        RequireApproval(by, approved);
        var journal = Read(ado);
        if (journal.Resetting || journal.StoryId != story.Id)
            throw new InvalidOperationException("Requirement is not the active managed demo story.");
        ado.VerifyOwnership(story, Marker(journal, "story"), "User Story");
        if (await ado.ForItem(story.Id).ReadAsync(token) != story)
            throw new InvalidOperationException("Requirement changed; check and approve again before opening a Bug.");
        if (state.Before is not { Passed: false } || !state.Before.Results.Any(result => !result.Passed))
            throw new InvalidOperationException("A real failing baseline is required before Bug creation.");
        if (journal.BugIds.Count > 0)
        {
            var existing = await ado.ForItem(journal.BugIds.Single()).ReadAsync(token);
            ado.VerifyOwnership(existing, Marker(journal, "bug"), "Bug");
            return existing;
        }
        var failures = string.Join("\n", state.Before.Results.Where(result => !result.Passed)
            .Select(result => $"{result.Requirement}: {result.Name}: {result.Error}"));
        return await CreateTrackedAsync(journal, ado, "bug", new(0, 0, null,
            $"Demo baseline failure: {story.Title}",
            $"Run {state.Id}; creation approved by {by}. Seeded private-workspace defect, not a production incident.\n" +
            $"Authoritative requirement: {story.Url}\nRounding: {state.Rounding}\nActual protected acceptance failures:\n{failures}", ""),
            story, token);
    }

    public async Task<WorkItem> ResetAsync(AdoClient ado, string by, bool approved, CancellationToken token)
    {
        RequireApproval(by, approved);
        var journal = Read(ado);
        // Finish uncertain creates by marker before deleting anything; never broaden the deletion allow-list.
        if (journal.PendingCreate is not null)
        {
            var kind = journal.PendingCreate;
            var recovered = await ado.FindOwnedAsync(Marker(journal, kind), token)
                ?? throw new InvalidOperationException("Pending create is unresolved. Inspect ADO; reset cannot guess or discard its journal.");
            ado.VerifyOwnership(recovered, Marker(journal, kind), kind == "story" ? "User Story" : "Bug");
            if (kind == "story") journal.StoryId = recovered.Id;
            else if (!journal.BugIds.Contains(recovered.Id)) journal.BugIds.Add(recovered.Id);
            journal.PendingCreate = null;
            Save(journal);
        }
        if (journal.StoryId is null) throw new InvalidOperationException("No tracked story to reset.");
        journal.Resetting = true;
        Save(journal);
        foreach (var id in journal.BugIds.Append(journal.StoryId.Value))
        {
            if (journal.DeletedIds.Contains(id)) continue;
            await ado.ForItem(id).SoftDeleteOwnedAsync(Marker(journal, id == journal.StoryId ? "story" : "bug"),
                id == journal.StoryId ? "User Story" : "Bug", token);
            journal.DeletedIds.Add(id);
            Save(journal);
        }
        foreach (var name in new[] { "workspace", "artifacts", "export", "runtime" })
        {
            var path = Path.Combine(store.DirectoryPath, name);
            store.EnsureLocalPath(path);
            if (Directory.Exists(path))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
                    store.EnsureLocalPath(entry);
                Directory.Delete(path, recursive: true);
            }
        }
        foreach (var path in Directory.EnumerateFiles(store.DirectoryPath)
            .Where(path => Path.GetFileName(path) is not ("managed.json" or ".lock")).ToArray())
        {
            store.EnsureLocalPath(path);
            File.Delete(path);
        }
        // Persist the new generation before creating: a failed recreation can be retried with create-story.
        Save(new ManagedJournal { Organization = journal.Organization, Project = journal.Project, OriginalTemplate = journal.OriginalTemplate });
        return await CreateStoryAsync(ado, by, approved, token);
    }

    public static void RequireApproval(string by, bool approved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        if (!approved) throw new InvalidOperationException("This remote action requires its explicit approval flag.");
    }
}
