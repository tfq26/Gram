using System.Text.Json;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class BuiltInConnectorTests
{
    [Fact]
    public async Task ControlledHttpRejectsNonPublicOrNonHttpsUrls()
    {
        var connector = new ControlledHttpConnector();
        var request = new ConnectorActionRequest("controlled-http", "post", JsonSerializer.SerializeToElement(new { url = "http://localhost:8080/hook", body = new { ok = true } }));
        var result = await connector.ExecuteAsync(request);
        Assert.False(result.Success);
        Assert.Equal("unsafe_url", result.Status);
    }

    [Fact]
    public void RegistryExposesReminderAndHttpActions()
    {
        var registry = new ConnectorRegistry([new ReminderConnector(new TaskAndMemoryService()), new ControlledHttpConnector()]);
        Assert.Contains(registry.ListActions("reminders"), action => action.Id == "create" && action.RequiresApproval);
        Assert.Contains(registry.ListActions("controlled-http"), action => action.Id == "post" && action.RiskLevel == ActionRiskLevel.Sensitive);
    }

    [Fact]
    public async Task DesktopUseIsDiscoverableButDisabledByDefault()
    {
        var previous = Environment.GetEnvironmentVariable("GRAM_ENABLE_DESKTOP_USE");
        Environment.SetEnvironmentVariable("GRAM_ENABLE_DESKTOP_USE", null);
        try
        {
            var connector = new DesktopUseConnector();
            Assert.False(connector.Descriptor.IsEnabled);
            Assert.Contains(connector.Actions, action => action.Id == "observe_screen" && action.RiskLevel == ActionRiskLevel.Safe);
            Assert.Contains(connector.Actions, action => action.Id == "type_text" && action.RiskLevel == ActionRiskLevel.Sensitive && action.RequiresApproval);
            Assert.Equal("feature_disabled", (await connector.ExecuteAsync(new ConnectorActionRequest("desktop-use", "observe_screen", JsonSerializer.SerializeToElement(new { })))).Status);
        }
        finally { Environment.SetEnvironmentVariable("GRAM_ENABLE_DESKTOP_USE", previous); }
    }
}
