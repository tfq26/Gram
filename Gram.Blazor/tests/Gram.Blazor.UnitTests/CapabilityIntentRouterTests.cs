using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class CapabilityIntentRouterTests
{
    [Fact]
    public void ChoosesGenericHttpInsteadOfCreatingAnApplicationConnector()
    {
        var router = new CapabilityIntentRouter(new ConnectorRegistry([]));
        var result = router.Resolve("Send a POST to https://example.test/hooks");
        Assert.Equal(CapabilityResolutionKind.GenericCapability, result.Kind);
        Assert.Equal("controlled-http", result.Capability);
        Assert.True(result.RequiresApproval);
    }

    [Fact]
    public void ChoosesConnectorGenerationWhenServiceIsMentionedButUnavailable()
    {
        var registry = new ConnectorRegistry([new FakeConnector()]);
        var result = new CapabilityIntentRouter(registry).Resolve("Connect to Example CRM and create a contact");
        Assert.Equal(CapabilityResolutionKind.GenerateConnector, result.Kind);
        Assert.Equal("example-crm", result.ConnectorId);
    }

    private sealed class FakeConnector : IConnectedConnector
    {
        public ConnectorDescriptor Descriptor => new("example-crm", "Example CRM", ConnectorTransport.Mcp, "Test", true);
        public IReadOnlyList<ConnectorAction> Actions => [];
        public Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new ConnectorActionResult(false, "not_implemented"));
    }
}
