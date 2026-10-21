using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Demo.Agent;

public sealed class AdoCreateRejectedException(string message) : InvalidOperationException(message);

public sealed record WorkItem(int Id, int Revision, string? Url, string Title,
    string Description, string AcceptanceCriteria, string? Type = null, string? State = null,
    string? Tags = null, string? Project = null)
{
    public bool Offline => Id == 0 && Url is null;
}

public sealed partial class AdoClient(HttpClient http, string organization, string project, int workItemId)
{
    public string Organization => organization;
    public string Project => project;
    public AdoClient ForItem(int id) => new(http, organization, project, id);

    public static AdoClient FromEnvironment(HttpClient http, Func<string, string?>? environment = null,
        bool requireItem = true)
    {
        var env = environment ?? Environment.GetEnvironmentVariable;
        string Required(string name) => !string.IsNullOrWhiteSpace(env(name))
            ? env(name)! : throw new InvalidOperationException($"Set {name} before using live ADO.");
        var organization = Required("ADO_ORGANIZATION");
        if (!OrganizationPattern().IsMatch(organization))
            throw new ArgumentException("ADO_ORGANIZATION must be the organization name, not a URL.");
        var id = 0;
        if (requireItem && (!int.TryParse(Required("ADO_WORK_ITEM_ID"), out id) || id <= 0))
            throw new ArgumentException("ADO_WORK_ITEM_ID must be a positive integer.");
        var pat = env("ADO_PAT");
        http.DefaultRequestHeaders.Authorization = !string.IsNullOrWhiteSpace(pat)
            ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + pat)))
            : new AuthenticationHeaderValue("Bearer", Required("ADO_TOKEN"));
        return new(http, organization, Required("ADO_PROJECT"), id);
    }

    private string Resource => $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{workItemId}";
    private string BrowserUrl => $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_workitems/edit/{workItemId}";

    public async Task<WorkItem> ReadAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(Resource + "?api-version=7.1", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return Parse(document.RootElement);
    }

    private WorkItem Parse(JsonElement root)
    {
        var fields = root.GetProperty("fields");
        string Field(string name) => fields.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()! : throw new InvalidDataException($"ADO ticket requires {name}.");
        if (root.GetProperty("id").GetInt32() != workItemId)
            throw new InvalidDataException("ADO returned a different work item.");
        string? Optional(string name) => fields.TryGetProperty(name, out var value) ? value.GetString() : null;
        return new(workItemId, root.GetProperty("rev").GetInt32(), BrowserUrl,
            Field("System.Title"), Field("System.Description"),
            Optional("System.WorkItemType") == "Bug" ? Optional("Microsoft.VSTS.Common.AcceptanceCriteria") ?? "" :
                Field("Microsoft.VSTS.Common.AcceptanceCriteria"),
            Optional("System.WorkItemType"), Optional("System.State"), Optional("System.Tags"), Optional("System.TeamProject"));
    }

    public async Task<WorkItem> AddHistoryAsync(WorkItem approved, string text, CancellationToken cancellationToken,
        bool preformatted = false)
    {
        if (approved.Offline || approved.Id != workItemId || approved.Url != BrowserUrl)
            throw new InvalidOperationException("ADO settings do not match this run's ticket.");
        // A revision test makes an ambiguous retry fail instead of writing twice.
        object[] patch =
        [
            new { op = "test", path = "/rev", value = (object)approved.Revision },
            new { op = "add", path = "/fields/System.History", value = (object)(preformatted
                ? $"<pre>{WebUtility.HtmlEncode(text)}</pre>" : WebUtility.HtmlEncode(text)) }
        ];
        using var request = new HttpRequestMessage(HttpMethod.Patch, Resource + "?api-version=7.1")
        {
            Content = JsonContent.Create(patch, mediaType: new MediaTypeHeaderValue("application/json-patch+json"))
        };
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ADO update failed (HTTP {(int)response.StatusCode}). " +
                "If the ticket changed or an earlier request timed out, inspect its History and start a fresh run; do not blindly retry.");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return Parse(document.RootElement);
    }

    private string Api => $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/wit";

    public async Task<WorkItem?> FindOwnedAsync(string marker, CancellationToken token)
    {
        if (!Regex.IsMatch(marker, "^copilot-demo-[a-f0-9]{32}-(story|bug)$"))
            throw new ArgumentException("Invalid ownership marker.");
        using var response = await http.PostAsJsonAsync(Api + "/wiql?api-version=7.1",
            new { query = $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.Tags] CONTAINS '{marker}'" }, token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var ids = document.RootElement.GetProperty("workItems").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32()).ToArray();
        if (ids.Length > 1) throw new InvalidOperationException("Multiple ownership matches; inspect ADO. No automatic writes allowed.");
        return ids.Length == 0 ? null : await ForItem(ids[0]).ReadAsync(token);
    }

    public async Task<WorkItem> CreateAsync(string type, WorkItem template, string marker,
        WorkItem? parent, CancellationToken token)
    {
        if (type is not ("User Story" or "Bug")) throw new ArgumentException("Unsupported work item type.");
        var patch = new List<object>
        {
            new { op = "add", path = "/fields/System.Title", value = template.Title },
            new { op = "add", path = "/fields/System.Description", value = WebUtility.HtmlEncode(template.Description) },
            new { op = "add", path = "/fields/System.Tags", value = marker }
        };
        if (type == "User Story")
            patch.Add(new { op = "add", path = "/fields/Microsoft.VSTS.Common.AcceptanceCriteria", value = template.AcceptanceCriteria });
        if (parent is not null)
        {
            var current = await ForItem(parent.Id).ReadAsync(token);
            if (current != parent) throw new AdoCreateRejectedException("Requirement changed; check and approve again before opening a Bug.");
            patch.Add(new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = "System.LinkTypes.Hierarchy-Reverse",
                    url = $"{Api}/workitems/{parent.Id}",
                    attributes = new { comment = "Demo baseline failure; requirement remains authoritative." }
                }
            });
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, Api + "/workitems/$" + Uri.EscapeDataString(type) + "?api-version=7.1")
        {
            Content = JsonContent.Create(patch, mediaType: new MediaTypeHeaderValue("application/json-patch+json"))
        };
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed or HttpStatusCode.UnprocessableEntity)
            throw new AdoCreateRejectedException($"ADO rejected create (HTTP {(int)response.StatusCode}); no item was created. Correct configuration/permissions, then retry the same action.");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return ForItem(document.RootElement.GetProperty("id").GetInt32()).Parse(document.RootElement);
    }

    public async Task<WorkItem> ResolveAsync(WorkItem approved, string history, CancellationToken token)
    {
        if (approved.Id != workItemId || approved.Url != BrowserUrl || approved.Type != "Bug")
            throw new InvalidOperationException("Resolution target does not match the approved Bug.");
        object[] patch =
        [
            new { op = "test", path = "/rev", value = (object)approved.Revision },
            new { op = "add", path = "/fields/System.History", value = (object)WebUtility.HtmlEncode(history) },
            new { op = "add", path = "/fields/System.State", value = (object)"Resolved" }
        ];
        using var request = new HttpRequestMessage(HttpMethod.Patch, Resource + "?api-version=7.1")
        {
            Content = JsonContent.Create(patch, mediaType: new MediaTypeHeaderValue("application/json-patch+json"))
        };
        using var response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return Parse(document.RootElement);
    }

    public async Task SoftDeleteOwnedAsync(string marker, string type, CancellationToken token)
    {
        WorkItem current;
        try { current = await ReadAsync(token); }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            using var recycled = await http.GetAsync(Api + $"/recyclebin/{workItemId}?api-version=7.1", token);
            recycled.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await recycled.Content.ReadAsStringAsync(token));
            current = Parse(document.RootElement.GetProperty("resource"));
            VerifyOwnership(current, marker, type);
            return;
        }
        VerifyOwnership(current, marker, type);
        // ADO soft-delete has no revision-test operation. Re-read ownership immediately before deletion.
        using var response = await http.DeleteAsync(Resource + "?api-version=7.1", token);
        response.EnsureSuccessStatusCode();
    }

    public void VerifyOwnership(WorkItem item, string marker, string type)
    {
        if (item.Id <= 0 || item.Project != project || item.Type != type ||
            item.Url != ForItem(item.Id).BrowserUrl ||
            !(item.Tags ?? "").Split(';', StringSplitOptions.TrimEntries).Contains(marker, StringComparer.Ordinal))
            throw new InvalidOperationException("Work item ownership, project or type does not match the persisted demo journal.");
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex OrganizationPattern();
}
