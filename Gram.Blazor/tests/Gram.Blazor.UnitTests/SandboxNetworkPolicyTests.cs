using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SandboxNetworkPolicyTests
{
    [Fact]
    public void DisabledNetwork_BlocksDirectNetworkUtilities()
    {
        Assert.False(SandboxNetworkPolicy.CanExecuteCommand("curl https://example.com", SandboxNetworkMode.Disabled, [], out var reason));
        Assert.Contains("disabled", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllowlistedNetwork_OnlyAllowsConfiguredHosts()
    {
        Assert.True(SandboxNetworkPolicy.CanExecuteCommand("curl https://api.example.com/data", SandboxNetworkMode.Allowlisted, ["example.com"], out _));
        Assert.False(SandboxNetworkPolicy.CanExecuteCommand("curl https://evil.example.net", SandboxNetworkMode.Allowlisted, ["example.com"], out _));
    }

    [Theory]
    [InlineData("OPENAI_API_KEY")]
    [InlineData("SERVICE_TOKEN")]
    [InlineData("DB_PASSWORD")]
    public void SensitiveEnvironmentNames_AreBlocked(string name) => Assert.True(SandboxNetworkPolicy.IsSensitiveEnvironmentVariable(name));
}
