using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ConnectorRegistryTests
{
    [Fact]
    public void BuiltInConnectorIdsCannotBeReplacedOrRemoved()
    {
        var builtIn = new FakeConnector("reserved");
        var registry = new ConnectorRegistry([builtIn]);

        Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeConnector("reserved")));
        Assert.False(registry.Unregister("reserved"));
        Assert.Same(builtIn, registry.Get("reserved"));
    }

    [Fact]
    public void DynamicConnectorIdsCannotBeOverwritten()
    {
        var registry = new ConnectorRegistry([]);
        registry.Register(new FakeConnector("generated"));

        Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeConnector("generated")));
    }

    private sealed class FakeConnector(string id) : IConnectedConnector
    {
        public ConnectorDescriptor Descriptor { get; } = new(id, id, ConnectorTransport.Native, "", false);
        public IReadOnlyList<ConnectorAction> Actions => [];
        public Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new ConnectorActionResult(true, "ok"));
    }
}
