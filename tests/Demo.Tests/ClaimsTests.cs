using Claims.Service;
using Xunit;

namespace Demo.Tests;

public class ClaimsTests
{
    [Theory]
    [InlineData(6000, 0, 0)]
    [InlineData(6000, 59, 0)]
    [InlineData(6000, 60, 1500)]
    [InlineData(6000, 119, 1500)]
    [InlineData(6000, 120, 3000)]
    [InlineData(0, 120, 0)]
    [InlineData(1_000_000_000, 1_000_000, 500_000_000)]
    public void Compensation_matches_boundaries(long fare, int delay, long expected) =>
        Assert.Equal(expected, CompensationPolicy.Calculate(fare, delay, Rounding.Down));

    [Theory]
    [InlineData(Rounding.Down, 250)]
    [InlineData(Rounding.Up, 251)]
    [InlineData(Rounding.Nearest, 251)]
    public void Rounding_is_an_explicit_input(Rounding rounding, long expected) =>
        Assert.Equal(expected, CompensationPolicy.Calculate(1003, 60, rounding));

    [Fact]
    public void Nearest_rounds_half_rappen_up() =>
        Assert.Equal(251, CompensationPolicy.Calculate(1002, 60, Rounding.Nearest));

    [Fact]
    public async Task Concurrent_retries_create_one_entry()
    {
        var ledger = new ClaimLedger(Rounding.Down);
        var claim = new Claim("claim-1", 6000, 60);
        var results = await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => ledger.Submit(claim))));
        Assert.All(results, item => Assert.Equal(1500, item.CompensationRappen));
        Assert.Single(ledger.Entries);
        Assert.Equal(results[0], ledger.Submit(claim));
    }

    [Fact]
    public void Conflicting_reuse_preserves_original()
    {
        var ledger = new ClaimLedger(Rounding.Down);
        var original = ledger.Submit(new Claim("claim-1", 6000, 60));
        Assert.Throws<ClaimConflictException>(() => ledger.Submit(new Claim("claim-1", 6001, 60)));
        Assert.Equal(original, Assert.Single(ledger.Entries));
    }

    [Theory]
    [InlineData(-1, 60)]
    [InlineData(1_000_000_001, 60)]
    [InlineData(6000, -1)]
    [InlineData(6000, 1_000_001)]
    public void Invalid_ranges_do_not_mutate_ledger(long fare, int delay)
    {
        var ledger = new ClaimLedger(Rounding.Down);
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Submit(new Claim("invalid", fare, delay)));
        Assert.Empty(ledger.Entries);
    }

    [Theory]
    [InlineData("{\"id\":\"x\",\"fareRappen\":1.5,\"delayMinutes\":60}")]
    [InlineData("{\"id\":\"x\",\"fareRappen\":6000,\"delayMinutes\":60.5}")]
    [InlineData("{\"id\":\"x\",\"fareRappen\":6000}")]
    [InlineData("{\"fareRappen\":6000,\"delayMinutes\":60}")]
    public void Malformed_claim_is_rejected(string json) =>
        Assert.Throws<System.Text.Json.JsonException>(() => Claim.Parse(json));

    [Fact]
    public void Invalid_rounding_is_not_a_default() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CompensationPolicy.Calculate(0, 0, (Rounding)99));
}
