using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claims.Service;

public sealed record Claim(
    [property: JsonRequired] string Id,
    [property: JsonRequired] long FareRappen,
    [property: JsonRequired] int DelayMinutes)
{
    public static Claim Parse(string json) =>
        JsonSerializer.Deserialize<Claim>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new JsonException("A claim object is required.");
}

public sealed record ClaimResult(string Id, long CompensationRappen);
public sealed class ClaimConflictException(string id) : Exception($"Claim '{id}' already exists with different details.");

public sealed class ClaimLedger(Rounding rounding)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, (Claim Claim, ClaimResult Result)> entries = [];

    public IReadOnlyList<ClaimResult> Entries
    {
        get { lock (gate) return entries.Values.Select(entry => entry.Result).ToArray(); }
    }

    public ClaimResult Submit(Claim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentException.ThrowIfNullOrWhiteSpace(claim.Id);
        var amount = CompensationPolicy.Calculate(claim.FareRappen, claim.DelayMinutes, rounding);
        lock (gate)
        {
            if (entries.TryGetValue(claim.Id, out var existing))
            {
                if (existing.Claim != claim) throw new ClaimConflictException(claim.Id);
                return existing.Result;
            }
            var result = new ClaimResult(claim.Id, amount);
            entries.Add(claim.Id, (claim, result));
            return result;
        }
    }
}
