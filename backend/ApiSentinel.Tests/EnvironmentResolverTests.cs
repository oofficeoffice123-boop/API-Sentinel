using ApiSentinel.Api.Services;

namespace ApiSentinel.Tests;

public sealed class EnvironmentResolverTests
{
    [Fact]
    public void Resolve_replaces_environment_variables_in_text()
    {
        var values = new Dictionary<string, EnvironmentValue> { ["HOST"] = new("https://api.example.test", false), ["TOKEN"] = new("sample-secret", true) };
        Assert.Equal("https://api.example.test/v1?token=sample-secret", EnvironmentResolver.Resolve("{{HOST}}/v1?token={{TOKEN}}", values));
    }

    [Fact]
    public void Resolve_fails_on_missing_variables_instead_of_sending_unresolved_template()
    {
        Assert.Throws<ArgumentException>(() => EnvironmentResolver.Resolve("https://{{MISSING}}", new Dictionary<string, EnvironmentValue>()));
    }
}