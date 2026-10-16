using System.Text.Json;
using Claims.Service;

if (args.Length != 1 || !Enum.TryParse<Rounding>(args[0], true, out var rounding) || !Enum.IsDefined(rounding))
{
    Console.Error.WriteLine("Usage: Claims.Acceptance down|up|nearest");
    return 2;
}

var results = new List<object>();
var failures = 0;
void Check(string requirement, string name, Action test)
{
    try
    {
        test();
        results.Add(new { requirement, name, passed = true, error = (string?)null });
    }
    catch (Exception error)
    {
        failures++;
        results.Add(new { requirement, name, passed = false, error = error.Message });
    }
}
void Equal(long expected, long actual)
{
    if (expected != actual) throw new InvalidOperationException($"Expected {expected} rappen; actual {actual}.");
}
void Reject<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

foreach (var (delay, expected) in new[] { (0, 0L), (59, 0L), (60, 1500L), (119, 1500L), (120, 3000L) })
    Check(delay < 60 ? "FR-001" : delay < 120 ? "FR-002" : "FR-003",
        $"Boundary at {delay} minutes", () => Equal(expected, CompensationPolicy.Calculate(6000, delay, rounding)));

Check("FR-004", "Explicit fractional-rappen rounding", () =>
    Equal(rounding == Rounding.Down ? 250 : 251, CompensationPolicy.Calculate(1003, 60, rounding)));
Check("FR-004", "Zero fare", () => Equal(0, CompensationPolicy.Calculate(0, 120, rounding)));
Check("FR-004", "Maximum fare and delay", () =>
    Equal(500_000_000, CompensationPolicy.Calculate(1_000_000_000, 1_000_000, rounding)));
Check("FR-005", "Sequential and concurrent retries", () =>
{
    var ledger = new ClaimLedger(rounding);
    var claim = new Claim("same", 6000, 120);
    Parallel.For(0, 100, _ => Equal(3000, ledger.Submit(claim).CompensationRappen));
    Equal(3000, ledger.Submit(claim).CompensationRappen);
    Equal(1, ledger.Entries.Count);
});
Check("FR-006", "Conflicting reuse leaves original unchanged", () =>
{
    var ledger = new ClaimLedger(rounding);
    ledger.Submit(new Claim("same", 6000, 120));
    Reject<ClaimConflictException>(() => ledger.Submit(new Claim("same", 6001, 120)));
    Equal(1, ledger.Entries.Count);
    Equal(3000, ledger.Entries[0].CompensationRappen);
});
Check("FR-007", "Invalid ranges and ID leave ledger empty", () =>
{
    var ledger = new ClaimLedger(rounding);
    foreach (var claim in new[] { new Claim("x", -1, 60), new Claim("x", 1_000_000_001, 60),
                 new Claim("x", 6000, -1), new Claim("x", 6000, 1_000_001) })
        Reject<ArgumentOutOfRangeException>(() => ledger.Submit(claim));
    Reject<ArgumentException>(() => ledger.Submit(new Claim(" ", 6000, 60)));
    Equal(0, ledger.Entries.Count);
});
Check("FR-007", "Non-integer and missing JSON fields rejected", () =>
{
    Reject<JsonException>(() => Claim.Parse("""{"id":"x","fareRappen":1.5,"delayMinutes":60}"""));
    Reject<JsonException>(() => Claim.Parse("""{"id":"x","fareRappen":6000,"delayMinutes":60.5}"""));
    Reject<JsonException>(() => Claim.Parse("""{"id":"x","fareRappen":6000}"""));
});

Console.WriteLine(JsonSerializer.Serialize(new { passed = failures == 0, rounding = rounding.ToString(), results }));
return failures == 0 ? 0 : 1;
