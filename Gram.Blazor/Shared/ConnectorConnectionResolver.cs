using System.Text.Json;

namespace Gram.Blazor.Shared;

public enum ConnectionResolutionStatus { Ready, ConnectionRequired, ReauthorizationRequired, InsufficientScope, LocalProfileRequired, UnsupportedAuthentication }
public sealed record ConnectionResolution(ConnectionResolutionStatus Status, string Message, ConnectorConnection? Connection = null, IReadOnlyList<string>? MissingScopes = null);

public interface IConnectorConnectionStore
{
    Task<IReadOnlyList<ConnectorConnection>> ListAsync(string userId, string? connectorId = null, CancellationToken cancellationToken = default);
    Task<ConnectorConnection> SaveAsync(ConnectorConnection connection, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string userId, string connectorId, string connectionId, CancellationToken cancellationToken = default);
}

public sealed class ConnectorConnectionStore : IConnectorConnectionStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public ConnectorConnectionStore(StorageService storage) => _path = storage.GetFilePath("connector_connections.json");
    public async Task<IReadOnlyList<ConnectorConnection>> ListAsync(string userId, string? connectorId = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return (await LoadAsync(cancellationToken)).Where(c => c.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase) && (connectorId == null || c.ConnectorId.Equals(connectorId, StringComparison.OrdinalIgnoreCase))).ToList(); }
        finally { _gate.Release(); }
    }
    public async Task<ConnectorConnection> SaveAsync(ConnectorConnection connection, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAsync(cancellationToken);
            all.RemoveAll(c => c.Id.Equals(connection.Id, StringComparison.OrdinalIgnoreCase) && c.UserId.Equals(connection.UserId, StringComparison.OrdinalIgnoreCase));
            all.Add(connection);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(all, BackendJson.Options), cancellationToken);
            return connection;
        }
        finally { _gate.Release(); }
    }
    public async Task<bool> DeleteAsync(string userId, string connectorId, string connectionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAsync(cancellationToken);
            var removed = all.RemoveAll(c => c.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase) && c.ConnectorId.Equals(connectorId, StringComparison.OrdinalIgnoreCase) && c.Id.Equals(connectionId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(all, BackendJson.Options), cancellationToken);
            return removed > 0;
        }
        finally { _gate.Release(); }
    }
    private async Task<List<ConnectorConnection>> LoadAsync(CancellationToken ct) { if (!File.Exists(_path)) return []; try { return JsonSerializer.Deserialize<List<ConnectorConnection>>(await File.ReadAllTextAsync(_path, ct), BackendJson.Options) ?? []; } catch { return []; } }
}

public interface IConnectorConnectionResolver
{
    Task<ConnectionResolution> ResolveAsync(string userId, ConnectorDescriptor connector, ConnectorAction action, string? requestedConnectionId = null, CancellationToken cancellationToken = default);
}

public sealed class ConnectorConnectionResolver : IConnectorConnectionResolver
{
    private readonly IConnectorConnectionStore _store;
    public ConnectorConnectionResolver(IConnectorConnectionStore store) => _store = store;
    public async Task<ConnectionResolution> ResolveAsync(string userId, ConnectorDescriptor connector, ConnectorAction action, string? requestedConnectionId = null, CancellationToken cancellationToken = default)
    {
        if (!connector.RequiresConnection) return new(ConnectionResolutionStatus.Ready, "No external account connection is required.");
        var connections = await _store.ListAsync(userId, connector.Id, cancellationToken);
        var connection = connections.FirstOrDefault(c => requestedConnectionId != null && c.Id.Equals(requestedConnectionId, StringComparison.OrdinalIgnoreCase)) ?? connections.FirstOrDefault(c => c.IsConnected);
        if (connection == null) return new(connector.Transport == ConnectorTransport.LocalAgent ? ConnectionResolutionStatus.LocalProfileRequired : ConnectionResolutionStatus.ConnectionRequired, connector.Transport == ConnectorTransport.LocalAgent ? "A paired local profile is required." : $"Connect {connector.Name} to continue.");
        if (!connection.IsConnected || connection.ExpiresAt <= DateTimeOffset.UtcNow) return new(ConnectionResolutionStatus.ReauthorizationRequired, $"Reconnect {connection.DisplayName} to continue.", connection);
        var required = action.RequiredScopes ?? [];
        var missing = required.Where(scope => !connection.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0) return new(ConnectionResolutionStatus.InsufficientScope, "The connected account does not grant the required permissions.", connection, missing);
        return new(ConnectionResolutionStatus.Ready, $"Using {connection.DisplayName}.", connection);
    }
}
