using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
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

    [Fact]
    public async Task DoesNotFallbackWhenRequestedConnectionIsUnknown()
    {
        var store = new FakeStore(new ConnectorConnection("c1", "spotify", "My Spotify", [], true, DateTimeOffset.UtcNow.AddHours(1), "u1"));
        var resolver = new ConnectorConnectionResolver(store);
        var connector = new SpotifyConnector().Descriptor;
        var action = new ConnectorAction("status", "spotify", "Playback status", "", ActionRiskLevel.Safe);

        var result = await resolver.ResolveAsync("u1", connector, action, "missing");

        Assert.Equal(ConnectionResolutionStatus.ConnectionRequired, result.Status);
        Assert.Null(result.Connection);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqliteConnectionStorePersistsAcrossInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gram-connection-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["GRAM_DATA_DIR"] = directory })
                .Build();
            var storage = new StorageService(configuration);
            var connection = new ConnectorConnection("c1", "fake", "Account", ["read"], true, DateTimeOffset.UtcNow.AddHours(1), "u1");

            await new ConnectorConnectionStore(storage).SaveAsync(connection);
            var restored = await new ConnectorConnectionStore(storage).ListAsync("u1", "fake");

            var item = Assert.Single(restored);
            Assert.Equal(connection.Id, item.Id);
            Assert.Equal(connection.Scopes, item.Scopes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }


    private sealed class FakeStore(ConnectorConnection connection) : IConnectorConnectionStore
    {
        public Task<IReadOnlyList<ConnectorConnection>> ListAsync(string userId, string? connectorId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ConnectorConnection>>([connection]);
        public Task<ConnectorConnection> SaveAsync(ConnectorConnection value, CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task<bool> DeleteAsync(string userId, string connectorId, string connectionId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
