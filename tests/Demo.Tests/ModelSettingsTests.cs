using Demo.Agent;
using Xunit;

namespace Demo.Tests;

public class ModelSettingsTests
{
    private static ModelSettings Read(params (string Key, string Value)[] settings)
    {
        var values = settings.ToDictionary(item => item.Key, item => item.Value);
        return ModelSettings.FromEnvironment(key => values.GetValueOrDefault(key));
    }

    [Fact]
    public void Explicit_model_is_required() =>
        Assert.Throws<InvalidOperationException>(() => Read());

    [Fact]
    public void No_provider_is_copilot_inference() =>
        Assert.Null(Read(("DEMO_MODEL", "chosen-model")).Provider);

    [Fact]
    public void Incomplete_byok_cannot_silently_fall_back_to_copilot() =>
        Assert.Throws<ArgumentException>(() => Read(("DEMO_MODEL", "chosen-model"), ("DEMO_MODEL_API_KEY", "test")));

    [Theory]
    [InlineData("http://untrusted.example/v1")]
    [InlineData("https://user:password@example.com/v1")]
    [InlineData("https://example.com/v1?api-key=test")]
    [InlineData("not-a-url")]
    public void Insecure_or_credential_bearing_endpoints_are_rejected(string endpoint) =>
        Assert.Throws<ArgumentException>(() => Read(("DEMO_MODEL", "chosen-model"), ("DEMO_MODEL_ENDPOINT", endpoint)));

    [Fact]
    public void Remote_provider_requires_explicit_credentials() =>
        Assert.Throws<InvalidOperationException>(() =>
            Read(("DEMO_MODEL", "chosen-model"), ("DEMO_MODEL_ENDPOINT", "https://gateway.example/v1")));

    [Fact]
    public void Configured_gateway_is_preserved()
    {
        var settings = Read(("DEMO_MODEL", "deployment"),
            ("DEMO_MODEL_ENDPOINT", "https://gateway.example/v1"),
            ("DEMO_MODEL_API_KEY", "test-only"), ("DEMO_WIRE_API", "responses"));
        Assert.Equal("deployment", settings.Model);
        Assert.Equal("https://gateway.example/v1", settings.Provider!.BaseUrl);
        Assert.Equal("responses", settings.Provider.WireApi);
    }
}
