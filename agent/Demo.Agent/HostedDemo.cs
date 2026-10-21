using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.AI.AgentServer.Invocations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Demo.Agent;

public sealed class HostedDemo(ILogger<HostedDemo> logger) : InvocationHandler
{
    public static void ValidateConfiguration()
    {
        if (Environment.GetEnvironmentVariable("DEMO_APPROVAL_TOKEN") is not { Length: >= 32 })
            throw new InvalidOperationException("serve requires DEMO_APPROVAL_TOKEN (at least 32 characters). " +
                "Supply it out of band to the presenter, never to the model.");
    }

    public override async Task HandleAsync(HttpRequest request, HttpResponse response,
        InvocationContext context, CancellationToken cancellationToken)
    {
        var expected = Environment.GetEnvironmentVariable("DEMO_APPROVAL_TOKEN") ?? "";
        var actual = request.Headers["X-Client-Demo-Approval"].ToString();
        if (expected.Length < 32 || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(actual))))
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            await response.WriteAsJsonAsync(new { error = "Presenter approval token required." }, cancellationToken);
            return;
        }
        using var activity = DemoTracing.Source.StartActivity("demo.command");
        try
        {
            var input = await request.ReadFromJsonAsync<CommandRequest>(cancellationToken)
                ?? throw new ArgumentException("Supply a JSON object with an args array.");
            if (input.Args is null || input.Args.Any(arg => arg is null))
                throw new ArgumentException("args must be an array of non-null strings.");
            string? Configuration(string name) => name switch
            {
                "ADO_PAT" or "DEMO_MODEL_API_KEY" => null,
                "ADO_TOKEN" => request.Headers["X-Client-Demo-Ado-Token"].ToString(),
                "DEMO_MODEL_BEARER_TOKEN" => request.Headers["X-Client-Demo-Model-Token"].ToString(),
                _ => Environment.GetEnvironmentVariable(name)
            };
            var state = await DemoCli.ExecuteCommandAsync(
                DemoCli.FindRoot(Environment.CurrentDirectory), input.Args, cancellationToken, Configuration);
            activity?.SetTag("demo.command", input.Args[0]);
            activity?.SetTag("demo.stage", state.Stage.ToString());
            var store = new RunStore(DemoCli.FindRoot(Environment.CurrentDirectory), state.Id);
            var exported = state.Stage is RunStage.Exported or RunStage.Published;
            await response.WriteAsJsonAsync(new
            {
                state,
                review = exported ? File.ReadAllText(Path.Combine(store.DirectoryPath, "export", "review.md")) : null,
                candidatePolicy = exported ? File.ReadAllText(Path.Combine(store.DirectoryPath, "export", "CompensationPolicy.cs")) : null
            }, RunStore.Json, cancellationToken);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or IOException or HttpRequestException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", error.GetType().Name);
            logger.LogError(error, "Demo invocation {InvocationId} failed", context.InvocationId);
            response.StatusCode = error is ArgumentException or JsonException
                ? StatusCodes.Status400BadRequest : StatusCodes.Status409Conflict;
            await response.WriteAsJsonAsync(new { error = error.Message }, cancellationToken);
        }
    }

    public sealed record CommandRequest(string[]? Args);
}
