using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Demo.Agent;

public sealed record FoundryDeployment(string Subscription, string Endpoint, string Session);

public static class FoundryClient
{
    public static async Task<JsonElement> RunAsync(string root, string[] args, CancellationToken cancellationToken)
    {
        var deployment = JsonSerializer.Deserialize<FoundryDeployment>(
            await File.ReadAllTextAsync(Path.Combine(root, ".azure", "hosted-demo.json"), cancellationToken),
            RunStore.Json) ?? throw new InvalidDataException("The Foundry deployment record is empty.");
        var approval = (await File.ReadAllTextAsync(
            Path.Combine(root, ".azure", "hosted-approval-token"), cancellationToken)).Trim();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        return await InvokeAsync(deployment, approval, args, async (resource, subscription, token) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var result = await ProcessRunner.RunAsync("az",
                ["account", "get-access-token", "--resource", resource, "--subscription", subscription,
                    "--only-show-errors", "--output", "json"], root, timeout.Token);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"Azure CLI authentication failed: {result.Error.Trim()}");
            using var document = JsonDocument.Parse(result.Output);
            var accessToken = document.RootElement.GetProperty("accessToken").GetString();
            return !string.IsNullOrWhiteSpace(accessToken) ? accessToken
                : throw new InvalidDataException("Azure CLI returned an empty access token.");
        }, http, cancellationToken);
    }

    public static async Task<JsonElement> InvokeAsync(FoundryDeployment deployment, string approval, string[] args,
        Func<string, string, CancellationToken, Task<string>> acquireToken, HttpClient http,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            throw new ArgumentException("Supply a remote command and run ID, e.g. remote status live1.");
        if (!Uri.TryCreate(deployment.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !endpoint.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase) ||
            !endpoint.IsDefaultPort || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new InvalidDataException("Invalid Foundry endpoint in the local deployment record.");
        if (string.IsNullOrWhiteSpace(deployment.Subscription) || string.IsNullOrWhiteSpace(deployment.Session))
            throw new InvalidDataException("The deployment has no subscription/session. Inspect its status before invoking it.");
        if (approval.Length < 32)
            throw new InvalidDataException("The presenter approval token must contain at least 32 characters.");

        if (args[0] is "start" or "fix")
            return await RunPresenterCommandAsync(args, async command =>
                await InvokeAsync(deployment, approval, command, acquireToken, http, cancellationToken));

        var url = deployment.Endpoint.TrimEnd('/') + "/endpoint/protocols/invocations?api-version=v1&agent_session_id=" +
            Uri.EscapeDataString(deployment.Session);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { args })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await acquireToken("https://ai.azure.com", deployment.Subscription, cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Client-Demo-Approval", approval);
        if (args[0] is not ("init-offline" or "clarify" or "status" or "export"))
            request.Headers.Add("X-Client-Demo-Ado-Token",
                await acquireToken("499b84ac-1321-427f-aa17-267ca6975798", deployment.Subscription, cancellationToken));
        if (args is ["execute", _, "--mode", "sdk"])
            request.Headers.Add("X-Client-Demo-Model-Token",
                await acquireToken("https://cognitiveservices.azure.com", deployment.Subscription, cancellationToken));

        using var response = await http.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("Refusing to forward credentials across an HTTP redirect.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Invocation failed (HTTP {(int)response.StatusCode}): " +
                await response.Content.ReadAsStringAsync(cancellationToken) +
                "\nDo not blindly retry a remote-write command; inspect ADO and the same session first.");
        using var resultDocument = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return resultDocument.RootElement.Clone();
    }

    private static async Task<JsonElement> RunPresenterCommandAsync(string[] args,
        Func<string[], Task<JsonElement>> invoke)
    {
        var flag = args[0] == "start" ? "--approve-create" : "--approve-create-bug";
        var by = args switch
        {
            [_, _, var option, "--by", var presenter] when option == flag => presenter,
            [_, _, "--by", var presenter, var option] when option == flag => presenter,
            _ => throw new ArgumentException(
                $"Usage: remote {args[0]} <run-id> --by <presenter> {flag}. Explicit approval is required.")
        };
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        var id = args[1];
        async Task<JsonElement> Step(params string[] command)
        {
            Console.Error.WriteLine($"[demo] {command[0]} {id}");
            return await invoke(command);
        }
        static string Stage(JsonElement response) =>
            response.GetProperty("state").GetProperty("stage").GetString()
                ?? throw new InvalidDataException("Hosted response has no workflow stage.");
        static JsonElement WithMessage(JsonElement response, string message) =>
            JsonSerializer.SerializeToElement(new
            {
                state = response.GetProperty("state"),
                message,
                review = response.TryGetProperty("review", out var review) ? review : (JsonElement?)null,
                candidatePolicy = response.TryGetProperty("candidatePolicy", out var candidate) ? candidate : (JsonElement?)null,
                repair = response.TryGetProperty("repair", out var repair) ? repair : (JsonElement?)null
            }, RunStore.Json);

        if (args[0] == "start")
        {
            var setup = await Step("create-story", id, "--by", by, "--approve-create");
            foreach (var item in setup.GetProperty("state").GetProperty("events").EnumerateArray())
                Console.Error.WriteLine($"[demo] {item.GetProperty("outcome").GetString()}");
            await Step("init", id);
            var asked = await Step("ask", id, "--by", by);
            return WithMessage(asked,
                "Waiting for the PO's rounding decision. Open the Story link in the events, " +
                "replace FR-004 in Acceptance Criteria with 'FR-004: Rounding: down|up|nearest' " +
                "(one actual choice), save, then run demo fix with explicit Bug-creation approval.");
        }

        var current = await Step("check", id);
        if (Stage(current) == "Draft")
            return WithMessage(current, "Waiting for the PO's rounding decision. Save exactly one " +
                "'FR-004: Rounding: down', 'FR-004: Rounding: up', or 'FR-004: Rounding: nearest' " +
                "in the Story's Acceptance Criteria, then run fix again. No approval granted or Bug created.");
        if (Stage(current) == "AwaitTicketApproval")
            current = await Step("approve", id, "--by", by);
        if (Stage(current) == "Approved")
            current = await Step("test", id);
        if (Stage(current) == "BaselineFailed")
        {
            var baseline = current.GetProperty("state").GetProperty("before");
            if (baseline.GetProperty("passed").GetBoolean())
                throw new InvalidDataException("Refusing Bug creation: expected a failing baseline.");
            Console.Error.WriteLine("[demo] Real failing baseline:");
            foreach (var result in baseline.GetProperty("results").EnumerateArray()
                         .Where(result => !result.GetProperty("passed").GetBoolean()))
                Console.Error.WriteLine(
                    $"  FAIL {result.GetProperty("requirement").GetString()}: " +
                    $"{result.GetProperty("name").GetString()} - {result.GetProperty("error").GetString()}");
            var opened = await Step("open-bug", id, "--by", by, "--approve-create-bug");
            if (opened.GetProperty("state").TryGetProperty("bug", out var bug) && bug.ValueKind == JsonValueKind.Object)
                Console.Error.WriteLine($"[demo] Bug #{bug.GetProperty("id")}: {bug.GetProperty("url").GetString()}");
            current = await Step("execute", id, "--mode", "sdk");
        }
        if (Stage(current) is "AwaitPublicationApproval" or "Exported")
            return WithMessage(await Step("export", id, "--by", by),
                "Review candidatePolicy, review and state.after.results before running " +
                $"demo publish {id} --by <reviewer>. Nothing has been published by fix.");
        throw new InvalidOperationException(
            $"Cannot continue fix from {Stage(current)}. Inspect demo status {id}; " +
            "failed or interrupted execution is not automatically retried. No reset or publication performed.");
    }
}
