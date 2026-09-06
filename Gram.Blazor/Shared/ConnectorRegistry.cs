using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Gram.Blazor.Shared;

public sealed class ConnectorRegistry : IConnectorRegistry
{
    private readonly Dictionary<string, IConnectedConnector> _connectors;
    private readonly HashSet<string> _builtInIds;
    private readonly object _sync = new();

    public ConnectorRegistry(IEnumerable<IConnectedConnector> connectors)
    {
        _connectors = connectors.ToDictionary(x => x.Descriptor.Id, StringComparer.OrdinalIgnoreCase);
        _builtInIds = _connectors.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    public IReadOnlyList<ConnectorDescriptor> ListConnectors() { lock (_sync) return _connectors.Values.Select(x => x.Descriptor).ToList(); }
    public IReadOnlyList<ConnectorAction> ListActions(string? connectorId = null) { lock (_sync) return _connectors.Values.Where(x => connectorId == null || x.Descriptor.Id.Equals(connectorId, StringComparison.OrdinalIgnoreCase)).SelectMany(x => x.Actions).ToList(); }
    public IConnectedConnector? Get(string connectorId) { lock (_sync) return _connectors.GetValueOrDefault(connectorId); }
    public void Register(IConnectedConnector connector)
    {
        lock (_sync)
        {
            if (_builtInIds.Contains(connector.Descriptor.Id)) throw new InvalidOperationException($"Connector ID '{connector.Descriptor.Id}' is reserved by a built-in connector.");
            if (_connectors.ContainsKey(connector.Descriptor.Id)) throw new InvalidOperationException($"Connector ID '{connector.Descriptor.Id}' is already registered.");
            _connectors.Add(connector.Descriptor.Id, connector);
        }
    }

    public bool Unregister(string connectorId)
    {
        lock (_sync)
        {
            if (_builtInIds.Contains(connectorId)) return false;
            return _connectors.Remove(connectorId);
        }
    }
}

public sealed class ConnectorPermissionService : IActionPermissionService
{
    private readonly Dictionary<string, ActionApproval> _sessionApprovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActionApproval> _runApprovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<ActionApproval>> _onceApprovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActionApproval> _denials = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly string? _persistencePath;
    private readonly string? _sqliteConnectionString;

    public ConnectorPermissionService(StorageService? storage = null)
    {
        if (storage != null)
        {
            _persistencePath = storage.GetFilePath("connector_approvals.json");
            var sqlitePath = storage.GetFilePath("connector_approvals.db");
            _sqliteConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Default
            }.ToString();
            InitializeDatabase();
        }
        LoadPersistedState();
    }

    private void InitializeDatabase()
    {
        if (string.IsNullOrWhiteSpace(_sqliteConnectionString)) return;
        try
        {
            using var connection = new SqliteConnection(_sqliteConnectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA busy_timeout = 5000;
                CREATE TABLE IF NOT EXISTS connector_once_approvals (
                    id TEXT PRIMARY KEY,
                    base_key TEXT NOT NULL,
                    user_id TEXT NOT NULL,
                    connector_id TEXT NOT NULL,
                    action_id TEXT NOT NULL,
                    decision INTEGER NOT NULL,
                    scope INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    run_id TEXT,
                    connection_id TEXT,
                    param_hash TEXT NOT NULL,
                    consumed INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS ix_once_approvals_lookup 
                ON connector_once_approvals (base_key, consumed);";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Fallback gracefully
        }
    }

    public ActionRiskLevel Classify(ConnectorAction action) => action.RiskLevel;

    public bool RequiresApproval(ConnectorAction action) => action.RequiresApproval || action.RiskLevel != ActionRiskLevel.Safe;

    public Task<ActionApproval?> GetApprovalAsync(string userId, ConnectorActionRequest request, CancellationToken cancellationToken = default)
    {
        return TryConsumeApprovalAsync(userId, request, cancellationToken);
    }

    public Task<ActionApproval?> TryConsumeApprovalAsync(string userId, ConnectorActionRequest request, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var baseKey = BaseKey(userId, request);

            // Check if active explicit denial exists
            if (_denials.TryGetValue(baseKey, out var denial))
            {
                if (denial.ExpiresAt > now)
                    return Task.FromResult<ActionApproval?>(null);
                _denials.Remove(baseKey);
            }

            // 1. Try Once approvals (cross-process atomic consumption via SQLite if storage configured, else in-memory queue)
            if (!string.IsNullOrWhiteSpace(_sqliteConnectionString))
            {
                var consumed = TryConsumeOnceApprovalSqlite(baseKey, now);
                if (consumed != null) return Task.FromResult<ActionApproval?>(consumed);
            }
            else if (_onceApprovals.TryGetValue(baseKey, out var queue) && queue.Count > 0)
            {
                while (queue.Count > 0)
                {
                    var candidate = queue.Dequeue();
                    if (candidate.ExpiresAt > now && candidate.Decision == ApprovalDecision.Approve)
                    {
                        if (queue.Count == 0) _onceApprovals.Remove(baseKey);
                        PersistLocked();
                        return Task.FromResult<ActionApproval?>(candidate);
                    }
                }
                _onceApprovals.Remove(baseKey);
                PersistLocked();
            }

            // 2. Try Run approvals (valid for matching RunId until expiration)
            if (!string.IsNullOrWhiteSpace(request.RunId))
            {
                var runKey = RunKey(baseKey, request.RunId);
                if (_runApprovals.TryGetValue(runKey, out var runApproval))
                {
                    if (runApproval.ExpiresAt > now && runApproval.Decision == ApprovalDecision.Approve)
                    {
                        return Task.FromResult<ActionApproval?>(runApproval);
                    }
                    _runApprovals.Remove(runKey);
                    PersistLocked();
                }
            }

            // 3. Try Session approvals (in-memory only, strictly bound to active process session)
            if (_sessionApprovals.TryGetValue(baseKey, out var sessionApproval))
            {
                if (sessionApproval.ExpiresAt > now && sessionApproval.Decision == ApprovalDecision.Approve)
                {
                    return Task.FromResult<ActionApproval?>(sessionApproval);
                }
                _sessionApprovals.Remove(baseKey);
            }

            return Task.FromResult<ActionApproval?>(null);
        }
    }

    public Task<ActionApproval?> PeekApprovalAsync(string userId, ConnectorActionRequest request, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var baseKey = BaseKey(userId, request);

            if (_denials.TryGetValue(baseKey, out var denial) && denial.ExpiresAt > now)
                return Task.FromResult<ActionApproval?>(null);

            if (!string.IsNullOrWhiteSpace(_sqliteConnectionString))
            {
                var peeked = PeekOnceApprovalSqlite(baseKey, now);
                if (peeked != null) return Task.FromResult<ActionApproval?>(peeked);
            }
            else if (_onceApprovals.TryGetValue(baseKey, out var queue))
            {
                foreach (var item in queue)
                {
                    if (item.ExpiresAt > now && item.Decision == ApprovalDecision.Approve)
                        return Task.FromResult<ActionApproval?>(item);
                }
            }

            if (!string.IsNullOrWhiteSpace(request.RunId))
            {
                var runKey = RunKey(baseKey, request.RunId);
                if (_runApprovals.TryGetValue(runKey, out var runApproval) && runApproval.ExpiresAt > now && runApproval.Decision == ApprovalDecision.Approve)
                    return Task.FromResult<ActionApproval?>(runApproval);
            }

            if (_sessionApprovals.TryGetValue(baseKey, out var sessionApproval) && sessionApproval.ExpiresAt > now && sessionApproval.Decision == ApprovalDecision.Approve)
                return Task.FromResult<ActionApproval?>(sessionApproval);

            return Task.FromResult<ActionApproval?>(null);
        }
    }

    public Task<ActionApproval> RecordApprovalAsync(string userId, ConnectorActionRequest request, ApprovalDecision decision, ApprovalScope scope, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var paramHash = ComputeParametersHash(request.Parameters);
        var baseKey = BaseKey(userId, request);

        lock (_sync)
        {
            if (decision == ApprovalDecision.Deny)
            {
                // Purge all approvals matching this target key across all scopes
                _sessionApprovals.Remove(baseKey);
                _onceApprovals.Remove(baseKey);
                DeleteOnceApprovalsSqlite(baseKey);

                var runPrefix = baseKey + ":";
                var matchingRunKeys = _runApprovals.Keys.Where(k => k.StartsWith(runPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var k in matchingRunKeys) _runApprovals.Remove(k);

                var denial = new ActionApproval(
                    Guid.NewGuid().ToString("N"),
                    userId,
                    request.ConnectorId,
                    request.ActionId,
                    ApprovalDecision.Deny,
                    scope,
                    now,
                    now.AddMinutes(30),
                    request.RunId,
                    request.ConnectionId,
                    paramHash
                );
                _denials[baseKey] = denial;
                PersistLocked();
                return Task.FromResult(denial);
            }

            // Remove any stale denial
            _denials.Remove(baseKey);

            var expiresAt = scope switch
            {
                ApprovalScope.Once => now.AddMinutes(30),
                ApprovalScope.Run => now.AddHours(2),
                ApprovalScope.Session => now.AddHours(12),
                _ => now.AddHours(12)
            };

            var approval = new ActionApproval(
                Guid.NewGuid().ToString("N"),
                userId,
                request.ConnectorId,
                request.ActionId,
                ApprovalDecision.Approve,
                scope,
                now,
                expiresAt,
                request.RunId,
                request.ConnectionId,
                paramHash
            );

            switch (scope)
            {
                case ApprovalScope.Once:
                    if (!string.IsNullOrWhiteSpace(_sqliteConnectionString))
                    {
                        RecordOnceApprovalSqlite(baseKey, approval);
                    }
                    else
                    {
                        if (!_onceApprovals.TryGetValue(baseKey, out var queue))
                        {
                            queue = new Queue<ActionApproval>();
                            _onceApprovals[baseKey] = queue;
                        }
                        queue.Enqueue(approval);
                    }
                    break;

                case ApprovalScope.Run:
                    var runKey = RunKey(baseKey, request.RunId ?? "none");
                    _runApprovals[runKey] = approval;
                    break;

                case ApprovalScope.Session:
                    // Session approvals are in-memory only
                    _sessionApprovals[baseKey] = approval;
                    break;
            }

            PersistLocked();

            return Task.FromResult(approval);
        }
    }

    public Task<bool> RevokeApprovalAsync(string userId, ConnectorActionRequest request, CancellationToken cancellationToken = default)
    {
        var baseKey = BaseKey(userId, request);
        lock (_sync)
        {
            var removedSession = _sessionApprovals.Remove(baseKey);
            var removedOnce = _onceApprovals.Remove(baseKey);
            var removedDenial = _denials.Remove(baseKey);

            var runPrefix = baseKey + ":";
            var matchingRunKeys = _runApprovals.Keys.Where(k => k.StartsWith(runPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in matchingRunKeys) _runApprovals.Remove(k);

            var removedSqlite = DeleteOnceApprovalsSqlite(baseKey);

            var removed = removedSession || removedOnce || removedDenial || matchingRunKeys.Count > 0 || removedSqlite;
            if (removed) PersistLocked();
            return Task.FromResult(removed);
        }
    }

    private void RecordOnceApprovalSqlite(string baseKey, ActionApproval approval)
    {
        if (string.IsNullOrWhiteSpace(_sqliteConnectionString)) return;
        try
        {
            using var connection = new SqliteConnection(_sqliteConnectionString);
            connection.Open();
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA busy_timeout = 5000;";
            pragma.ExecuteNonQuery();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO connector_once_approvals 
                (id, base_key, user_id, connector_id, action_id, decision, scope, created_at, expires_at, run_id, connection_id, param_hash, consumed)
                VALUES ($id, $baseKey, $userId, $connectorId, $actionId, $decision, $scope, $createdAt, $expiresAt, $runId, $connectionId, $paramHash, 0);";
            cmd.Parameters.AddWithValue("$id", approval.Id);
            cmd.Parameters.AddWithValue("$baseKey", baseKey);
            cmd.Parameters.AddWithValue("$userId", approval.UserId);
            cmd.Parameters.AddWithValue("$connectorId", approval.ConnectorId);
            cmd.Parameters.AddWithValue("$actionId", approval.ActionId);
            cmd.Parameters.AddWithValue("$decision", (int)approval.Decision);
            cmd.Parameters.AddWithValue("$scope", (int)approval.Scope);
            cmd.Parameters.AddWithValue("$createdAt", approval.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$expiresAt", approval.ExpiresAt.ToString("O"));
            cmd.Parameters.AddWithValue("$runId", (object?)approval.RunId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$connectionId", (object?)approval.ConnectionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$paramHash", approval.ParameterHash ?? "empty");
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Fail closed
        }
    }

    private ActionApproval? TryConsumeOnceApprovalSqlite(string baseKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(_sqliteConnectionString)) return null;
        try
        {
            using var connection = new SqliteConnection(_sqliteConnectionString);
            connection.Open();
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA busy_timeout = 5000;";
            pragma.ExecuteNonQuery();

            using var beginCmd = connection.CreateCommand();
            beginCmd.CommandText = "BEGIN IMMEDIATE;";
            beginCmd.ExecuteNonQuery();

            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, user_id, connector_id, action_id, decision, scope, created_at, expires_at, run_id, connection_id, param_hash
                    FROM connector_once_approvals
                    WHERE base_key = $baseKey AND consumed = 0
                    ORDER BY datetime(created_at) ASC;";
                cmd.Parameters.AddWithValue("$baseKey", baseKey);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetString(0);
                    var userIdVal = reader.GetString(1);
                    var connectorId = reader.GetString(2);
                    var actionId = reader.GetString(3);
                    var decisionVal = (ApprovalDecision)reader.GetInt32(4);
                    var scopeVal = (ApprovalScope)reader.GetInt32(5);
                    var createdAtVal = DateTimeOffset.Parse(reader.GetString(6));
                    var expiresAtVal = DateTimeOffset.Parse(reader.GetString(7));
                    var runIdVal = reader.IsDBNull(8) ? null : reader.GetString(8);
                    var connectionIdVal = reader.IsDBNull(9) ? null : reader.GetString(9);
                    var paramHashVal = reader.GetString(10);

                    if (expiresAtVal <= now || decisionVal != ApprovalDecision.Approve)
                    {
                        using var expireCmd = connection.CreateCommand();
                        expireCmd.CommandText = "UPDATE connector_once_approvals SET consumed = 1 WHERE id = $id AND consumed = 0;";
                        expireCmd.Parameters.AddWithValue("$id", id);
                        expireCmd.ExecuteNonQuery();
                        continue;
                    }

                    reader.Close();

                    using var updateCmd = connection.CreateCommand();
                    updateCmd.CommandText = "UPDATE connector_once_approvals SET consumed = 1 WHERE id = $id AND consumed = 0;";
                    updateCmd.Parameters.AddWithValue("$id", id);
                    var rowsAffected = updateCmd.ExecuteNonQuery();

                    if (rowsAffected > 0)
                    {
                        using var commitCmd = connection.CreateCommand();
                        commitCmd.CommandText = "COMMIT;";
                        commitCmd.ExecuteNonQuery();

                        return new ActionApproval(
                            id, userIdVal, connectorId, actionId, decisionVal, scopeVal, createdAtVal, expiresAtVal, runIdVal, connectionIdVal, paramHashVal
                        );
                    }

                    using var rbCmd = connection.CreateCommand();
                    rbCmd.CommandText = "ROLLBACK;";
                    rbCmd.ExecuteNonQuery();
                    return null;
                }

                reader.Close();
                using var commitEmpty = connection.CreateCommand();
                commitEmpty.CommandText = "COMMIT;";
                commitEmpty.ExecuteNonQuery();
                return null;
            }
            catch
            {
                try
                {
                    using var rbFail = connection.CreateCommand();
                    rbFail.CommandText = "ROLLBACK;";
                    rbFail.ExecuteNonQuery();
                }
                catch { }
                throw;
            }
        }
        catch
        {
            return null;
        }
    }

    private ActionApproval? PeekOnceApprovalSqlite(string baseKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(_sqliteConnectionString)) return null;
        try
        {
            using var connection = new SqliteConnection(_sqliteConnectionString);
            connection.Open();
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA busy_timeout = 5000;";
            pragma.ExecuteNonQuery();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, user_id, connector_id, action_id, decision, scope, created_at, expires_at, run_id, connection_id, param_hash
                FROM connector_once_approvals
                WHERE base_key = $baseKey AND consumed = 0
                ORDER BY datetime(created_at) ASC;";
            cmd.Parameters.AddWithValue("$baseKey", baseKey);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var userIdVal = reader.GetString(1);
                var connectorId = reader.GetString(2);
                var actionId = reader.GetString(3);
                var decisionVal = (ApprovalDecision)reader.GetInt32(4);
                var scopeVal = (ApprovalScope)reader.GetInt32(5);
                var createdAtVal = DateTimeOffset.Parse(reader.GetString(6));
                var expiresAtVal = DateTimeOffset.Parse(reader.GetString(7));
                var runIdVal = reader.IsDBNull(8) ? null : reader.GetString(8);
                var connectionIdVal = reader.IsDBNull(9) ? null : reader.GetString(9);
                var paramHashVal = reader.GetString(10);

                if (expiresAtVal > now && decisionVal == ApprovalDecision.Approve)
                {
                    return new ActionApproval(
                        id, userIdVal, connectorId, actionId, decisionVal, scopeVal, createdAtVal, expiresAtVal, runIdVal, connectionIdVal, paramHashVal
                    );
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private bool DeleteOnceApprovalsSqlite(string baseKey)
    {
        if (string.IsNullOrWhiteSpace(_sqliteConnectionString)) return false;
        try
        {
            using var connection = new SqliteConnection(_sqliteConnectionString);
            connection.Open();
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA busy_timeout = 5000;";
            pragma.ExecuteNonQuery();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM connector_once_approvals WHERE base_key = $baseKey AND consumed = 0;";
            cmd.Parameters.AddWithValue("$baseKey", baseKey);
            var rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }
        catch
        {
            return false;
        }
    }

    private void LoadPersistedState()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath)) return;
        try
        {
            var entries = JsonSerializer.Deserialize<List<PersistedApproval>>(File.ReadAllText(_persistencePath)) ?? [];
            foreach (var entry in entries)
            {
                switch (entry.Kind)
                {
                    // ApprovalScope.Session is in-memory only and intentionally NOT loaded from disk
                    case "run": _runApprovals[entry.Key] = entry.Approval; break;
                    case "denial": _denials[entry.Key] = entry.Approval; break;
                    case "once":
                        // Only load from JSON if SQLite is not in use
                        if (string.IsNullOrWhiteSpace(_sqliteConnectionString))
                        {
                            if (!_onceApprovals.TryGetValue(entry.Key, out var queue))
                            {
                                queue = new Queue<ActionApproval>();
                                _onceApprovals[entry.Key] = queue;
                            }
                            queue.Enqueue(entry.Approval);
                        }
                        break;
                }
            }
        }
        catch
        {
            // Corrupt or partially written approval state must fail closed.
            _sessionApprovals.Clear();
            _runApprovals.Clear();
            _onceApprovals.Clear();
            _denials.Clear();
        }
    }

    private void PersistLocked()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath)) return;
        try
        {
            var entries = new List<PersistedApproval>();
            // Note: ApprovalScope.Session is in-memory only and intentionally NOT persisted to survive restart.
            entries.AddRange(_runApprovals.Select(x => new PersistedApproval("run", x.Key, x.Value)));
            entries.AddRange(_denials.Select(x => new PersistedApproval("denial", x.Key, x.Value)));
            if (string.IsNullOrWhiteSpace(_sqliteConnectionString))
            {
                entries.AddRange(_onceApprovals.SelectMany(x => x.Value.Select(value => new PersistedApproval("once", x.Key, value))));
            }
            var temporaryPath = _persistencePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries));
            File.Move(temporaryPath, _persistencePath, true);
        }
        catch
        {
            // Approval decisions remain enforced in memory if local persistence is unavailable.
        }
    }

    private sealed record PersistedApproval(string Kind, string Key, ActionApproval Approval);

    public static string BaseKey(string userId, ConnectorActionRequest request)
    {
        var user = string.IsNullOrWhiteSpace(userId) ? "default" : userId.Trim();
        var conn = string.IsNullOrWhiteSpace(request.ConnectionId) ? "default" : request.ConnectionId.Trim().ToLowerInvariant();
        var connector = (request.ConnectorId ?? string.Empty).Trim().ToLowerInvariant();
        var action = (request.ActionId ?? string.Empty).Trim().ToLowerInvariant();
        var paramHash = ComputeParametersHash(request.Parameters);
        return $"{user}:{connector}:{conn}:{action}:{paramHash}";
    }

    private static string RunKey(string baseKey, string runId) => $"{baseKey}:{runId.Trim()}";

    public static string ComputeParametersHash(JsonElement parameters)
    {
        if (parameters.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return "empty";

        var canonical = CanonicalizeJson(parameters);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string CanonicalizeJson(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(element, writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    WriteCanonical(prop.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l)) writer.WriteNumberValue(l);
                else if (element.TryGetDouble(out var d)) writer.WriteNumberValue(d);
                else writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
