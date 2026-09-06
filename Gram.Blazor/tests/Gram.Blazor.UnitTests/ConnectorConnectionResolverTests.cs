using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ConnectorConnectionResolverTests
{
    [Fact]
    public async Task SelectsVerifiedConnectionAndReportsMissingScopes()
    {
        var store = new FakeStore(new ConnectorConnection("c1", "spotify", "My Spotify", ["user-read-playback-state"], true, DateTimeOffset.UtcNow.AddHours(1), "u1"));
        var resolver = new ConnectorConnectionResolver(store);
        var connector = new SpotifyConnector().Descriptor;
        var action = new ConnectorAction("play", "spotify", "Play", "", ActionRiskLevel.Moderate, RequiresApproval: true, RequiredScopes: ["user-modify-playback-state"]);

        var result = await resolver.ResolveAsync("u1", connector, action);

        Assert.Equal(ConnectionResolutionStatus.InsufficientScope, result.Status);
        Assert.Contains("user-modify-playback-state", result.MissingScopes!);
        Assert.Equal("c1", result.Connection!.Id);
    }

    private sealed class FakeStore(ConnectorConnection connection) : IConnectorConnectionStore
    {
        public Task<IReadOnlyList<ConnectorConnection>> ListAsync(string userId, string? connectorId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ConnectorConnection>>([connection]);
        public Task<ConnectorConnection> SaveAsync(ConnectorConnection value, CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task<bool> DeleteAsync(string userId, string connectorId, string connectionId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
