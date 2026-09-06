using System.Text.Json;
using Microsoft.Data.Sqlite;

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
    private readonly string _connectionString;
    public ConnectorConnectionStore(StorageService storage)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = storage.GetFilePath("gram_connectors.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        Initialize();
    }

    public async Task<IReadOnlyList<ConnectorConnection>> ListAsync(string userId, string? connectorId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = connectorId == null
            ? "SELECT id, connector_id, display_name, scopes_json, is_connected, expires_at, user_id, authentication_type, last_verified_at, client_id FROM connector_connections WHERE user_id = @user_id ORDER BY last_verified_at DESC;"
            : "SELECT id, connector_id, display_name, scopes_json, is_connected, expires_at, user_id, authentication_type, last_verified_at, client_id FROM connector_connections WHERE user_id = @user_id AND connector_id = @connector_id ORDER BY last_verified_at DESC;";
        command.Parameters.AddWithValue("@user_id", userId);
        if (connectorId != null) command.Parameters.AddWithValue("@connector_id", connectorId);
        var result = new List<ConnectorConnection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadConnection(reader));
        return result;
    }

    public async Task<ConnectorConnection> SaveAsync(ConnectorConnection connection, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        await using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO connector_connections (id, connector_id, display_name, scopes_json, is_connected, expires_at, user_id, authentication_type, last_verified_at, client_id)
            VALUES (@id, @connector_id, @display_name, @scopes_json, @is_connected, @expires_at, @user_id, @authentication_type, @last_verified_at, @client_id)
            ON CONFLICT (user_id, connector_id, id) DO UPDATE SET
                display_name = @display_name, scopes_json = @scopes_json, is_connected = @is_connected,
                expires_at = @expires_at, authentication_type = @authentication_type,
                last_verified_at = @last_verified_at, client_id = @client_id;
            """;
        AddParameters(command, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    public async Task<bool> DeleteAsync(string userId, string connectorId, string connectionId, CancellationToken cancellationToken = default)
    {
        await using var db = await OpenAsync(cancellationToken);
        await using var command = db.CreateCommand();
        command.CommandText = "DELETE FROM connector_connections WHERE user_id = @user_id AND connector_id = @connector_id AND id = @id;";
        command.Parameters.AddWithValue("@user_id", userId);
        command.Parameters.AddWithValue("@connector_id", connectorId);
        command.Parameters.AddWithValue("@id", connectionId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS connector_connections (
                id TEXT NOT NULL,
                connector_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                scopes_json TEXT NOT NULL,
                is_connected INTEGER NOT NULL,
                expires_at TEXT NULL,
                user_id TEXT NOT NULL,
                authentication_type TEXT NOT NULL,
                last_verified_at TEXT NULL,
                client_id TEXT NULL,
                PRIMARY KEY (user_id, connector_id, id)
            );
            CREATE INDEX IF NOT EXISTS idx_connector_connections_user ON connector_connections(user_id, connector_id);
            """;
        command.ExecuteNonQuery();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddParameters(SqliteCommand command, ConnectorConnection connection)
    {
        command.Parameters.AddWithValue("@id", connection.Id);
        command.Parameters.AddWithValue("@connector_id", connection.ConnectorId);
        command.Parameters.AddWithValue("@display_name", connection.DisplayName);
        command.Parameters.AddWithValue("@scopes_json", JsonSerializer.Serialize(connection.Scopes));
        command.Parameters.AddWithValue("@is_connected", connection.IsConnected ? 1 : 0);
        command.Parameters.AddWithValue("@expires_at", connection.ExpiresAt?.ToString("o") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@user_id", connection.UserId);
        command.Parameters.AddWithValue("@authentication_type", connection.AuthenticationType);
        command.Parameters.AddWithValue("@last_verified_at", connection.LastVerifiedAt?.ToString("o") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@client_id", connection.ClientId ?? (object)DBNull.Value);
    }

    private static ConnectorConnection ReadConnection(SqliteDataReader reader)
    {
        var scopes = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [];
        return new ConnectorConnection(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), scopes,
            reader.GetInt32(4) == 1,
            reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
            reader.GetString(6), reader.GetString(7),
            reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
            reader.IsDBNull(9) ? null : reader.GetString(9));
    }
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
        var connection = requestedConnectionId == null
            ? connections.FirstOrDefault(c => c.IsConnected)
            : connections.FirstOrDefault(c => c.Id.Equals(requestedConnectionId, StringComparison.OrdinalIgnoreCase));
        if (requestedConnectionId != null && connection == null)
            return new(ConnectionResolutionStatus.ConnectionRequired, "The requested account connection was not found.");
        if (connection == null) return new(connector.Transport == ConnectorTransport.LocalAgent ? ConnectionResolutionStatus.LocalProfileRequired : ConnectionResolutionStatus.ConnectionRequired, connector.Transport == ConnectorTransport.LocalAgent ? "A paired local profile is required." : $"Connect {connector.Name} to continue.");
        if (!connection.IsConnected || connection.ExpiresAt <= DateTimeOffset.UtcNow) return new(ConnectionResolutionStatus.ReauthorizationRequired, $"Reconnect {connection.DisplayName} to continue.", connection);
        var required = action.RequiredScopes ?? [];
        var missing = required.Where(scope => !connection.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0) return new(ConnectionResolutionStatus.InsufficientScope, "The connected account does not grant the required permissions.", connection, missing);
        return new(ConnectionResolutionStatus.Ready, $"Using {connection.DisplayName}.", connection);
    }
}
