using System.Data;
using System.Text.Json;
using Gram.Blazor.Shared;
using Microsoft.Data.Sqlite;

namespace Gram.Desktop;

public sealed record OfflineQueuedEdit(
    string Id,
    string ConversationId,
    string EditType,
    string PayloadJson,
    DateTimeOffset QueuedAt,
    int ExpectedVersion = 0
);

public sealed class VersionConflictException : Exception
{
    public string ConversationId { get; }
    public int ServerVersion { get; }
    public int ExpectedVersion { get; }

    public VersionConflictException(string conversationId, int serverVersion, int expectedVersion)
        : base($"Version conflict for conversation '{conversationId}': Server is at version {serverVersion}, but local edit expected version {expectedVersion}.")
    {
        ConversationId = conversationId;
        ServerVersion = serverVersion;
        ExpectedVersion = expectedVersion;
    }
}

/// <summary>
/// Durable, local-only cache for desktop recovery and offline resilience.
/// Supports both transactional SQLite (.db) and fallback JSON snapshots.
/// Strictly never stores access tokens, refresh tokens, provider keys, or authorization headers.
/// </summary>
public sealed class DesktopOfflineCache
{
    private readonly string _path;
    private readonly bool _isSqlite;
    private readonly string? _sqliteConnectionString;
    private readonly object _gate = new();

    public DesktopOfflineCache(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gram", "desktop-cache.db");
        _isSqlite = _path.EndsWith(".db", StringComparison.OrdinalIgnoreCase);

        if (_isSqlite)
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            _sqliteConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Default
            }.ToString();

            InitializeSqliteDatabase();
        }
    }

    public string Path => _path;
    public bool IsSqlite => _isSqlite;

    private void InitializeSqliteDatabase()
    {
        if (string.IsNullOrWhiteSpace(_sqliteConnectionString)) return;
        try
        {
            using var conn = new SqliteConnection(_sqliteConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA busy_timeout = 5000;
                CREATE TABLE IF NOT EXISTS desktop_snapshots (
                    id TEXT PRIMARY KEY,
                    snapshot_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS offline_queued_edits (
                    id TEXT PRIMARY KEY,
                    conversation_id TEXT NOT NULL,
                    edit_type TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    queued_at TEXT NOT NULL,
                    expected_version INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS ix_queued_edits_conversation ON offline_queued_edits (conversation_id);
            ";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Graceful fallback if SQLite initialization encounters permission issues
        }
    }

    public DesktopCacheSnapshot Read()
    {
        lock (_gate)
        {
            if (_isSqlite && !string.IsNullOrWhiteSpace(_sqliteConnectionString))
            {
                try
                {
                    using var conn = new SqliteConnection(_sqliteConnectionString);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT snapshot_json FROM desktop_snapshots WHERE id = 'active_snapshot';";
                    var json = cmd.ExecuteScalar() as string;
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var snapshot = JsonSerializer.Deserialize<DesktopCacheSnapshot>(json, BackendJson.Options) ?? new();
                        var queued = GetQueuedEditsInternal(conn);
                        return snapshot with { QueuedEdits = queued };
                    }
                    return new DesktopCacheSnapshot();
                }
                catch
                {
                    return new DesktopCacheSnapshot();
                }
            }

            try
            {
                if (!File.Exists(_path)) return new();
                return JsonSerializer.Deserialize<DesktopCacheSnapshot>(File.ReadAllText(_path), BackendJson.Options) ?? new();
            }
            catch (JsonException) { return new(); }
            catch (IOException) { return new(); }
        }
    }

    public void Write(DesktopCacheSnapshot snapshot)
    {
        lock (_gate)
        {
            var sanitized = Sanitize(snapshot);

            if (_isSqlite && !string.IsNullOrWhiteSpace(_sqliteConnectionString))
            {
                try
                {
                    using var conn = new SqliteConnection(_sqliteConnectionString);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO desktop_snapshots (id, snapshot_json, updated_at)
                        VALUES ('active_snapshot', $json, $updatedAt)
                        ON CONFLICT(id) DO UPDATE SET snapshot_json = excluded.snapshot_json, updated_at = excluded.updated_at;
                    ";
                    cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(sanitized, BackendJson.Options));
                    cmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
                    cmd.ExecuteNonQuery();
                    return;
                }
                catch
                {
                    // Fall back to JSON file write if SQLite fails
                }
            }

            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(sanitized, BackendJson.Options));
            File.Move(temp, _path, true);
        }
    }

    public void EnqueueEdit(OfflineQueuedEdit edit)
    {
        lock (_gate)
        {
            if (_isSqlite && !string.IsNullOrWhiteSpace(_sqliteConnectionString))
            {
                try
                {
                    using var conn = new SqliteConnection(_sqliteConnectionString);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO offline_queued_edits (id, conversation_id, edit_type, payload_json, queued_at, expected_version)
                        VALUES ($id, $conversationId, $editType, $payloadJson, $queuedAt, $expectedVersion);
                    ";
                    cmd.Parameters.AddWithValue("$id", edit.Id);
                    cmd.Parameters.AddWithValue("$conversationId", edit.ConversationId);
                    cmd.Parameters.AddWithValue("$editType", edit.EditType);
                    cmd.Parameters.AddWithValue("$payloadJson", edit.PayloadJson);
                    cmd.Parameters.AddWithValue("$queuedAt", edit.QueuedAt.ToString("O"));
                    cmd.Parameters.AddWithValue("$expectedVersion", edit.ExpectedVersion);
                    cmd.ExecuteNonQuery();
                    return;
                }
                catch { }
            }

            var snapshot = Read();
            var list = new List<OfflineQueuedEdit>(snapshot.SafeQueuedEdits) { edit };
            Write(snapshot with { QueuedEdits = list });
        }
    }

    public IReadOnlyList<OfflineQueuedEdit> GetQueuedEdits(string? conversationId = null)
    {
        lock (_gate)
        {
            if (_isSqlite && !string.IsNullOrWhiteSpace(_sqliteConnectionString))
            {
                try
                {
                    using var conn = new SqliteConnection(_sqliteConnectionString);
                    conn.Open();
                    return GetQueuedEditsInternal(conn, conversationId);
                }
                catch
                {
                    return [];
                }
            }

            var snapshot = Read();
            return conversationId == null
                ? snapshot.SafeQueuedEdits
                : snapshot.SafeQueuedEdits.Where(e => e.ConversationId == conversationId).ToList();
        }
    }

    public void RemoveQueuedEdit(string editId)
    {
        lock (_gate)
        {
            if (_isSqlite && !string.IsNullOrWhiteSpace(_sqliteConnectionString))
            {
                try
                {
                    using var conn = new SqliteConnection(_sqliteConnectionString);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "DELETE FROM offline_queued_edits WHERE id = $id;";
                    cmd.Parameters.AddWithValue("$id", editId);
                    cmd.ExecuteNonQuery();
                    return;
                }
                catch { }
            }

            var snapshot = Read();
            var list = snapshot.SafeQueuedEdits.Where(e => e.Id != editId).ToList();
            Write(snapshot with { QueuedEdits = list });
        }
    }

    private static IReadOnlyList<OfflineQueuedEdit> GetQueuedEditsInternal(SqliteConnection conn, string? conversationId = null)
    {
        using var cmd = conn.CreateCommand();
        if (conversationId != null)
        {
            cmd.CommandText = "SELECT id, conversation_id, edit_type, payload_json, queued_at, expected_version FROM offline_queued_edits WHERE conversation_id = $cid ORDER BY datetime(queued_at) ASC;";
            cmd.Parameters.AddWithValue("$cid", conversationId);
        }
        else
        {
            cmd.CommandText = "SELECT id, conversation_id, edit_type, payload_json, queued_at, expected_version FROM offline_queued_edits ORDER BY datetime(queued_at) ASC;";
        }

        var results = new List<OfflineQueuedEdit>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new OfflineQueuedEdit(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.GetInt32(5)
            ));
        }
        return results;
    }

    private static DesktopCacheSnapshot Sanitize(DesktopCacheSnapshot snapshot)
    {
        // Never persist authorization tokens, refresh tokens, or provider secrets
        return snapshot with
        {
            Conversations = snapshot.Conversations?.Select(c => c with {}).ToList(),
            Models = snapshot.Models?.Select(m => m with {}).ToList(),
            ConversationDetails = snapshot.ConversationDetails?.Select(d => d with {}).ToList(),
            ActiveRun = snapshot.ActiveRun is { } r ? new RunRecoveryState(r.RunId, r.ConversationId, r.LastSequence, r.SavedAt) : null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}

public sealed record DesktopCacheSnapshot(
    IReadOnlyList<ConversationSummary>? Conversations = null,
    IReadOnlyList<ModelSummary>? Models = null,
    IReadOnlyList<ConversationDetail>? ConversationDetails = null,
    RunRecoveryState? ActiveRun = null,
    DateTimeOffset? UpdatedAt = null,
    IReadOnlyList<OfflineQueuedEdit>? QueuedEdits = null)
{
    public IReadOnlyList<ConversationSummary> SafeConversations => Conversations ?? [];
    public IReadOnlyList<ModelSummary> SafeModels => Models ?? [];
    public IReadOnlyList<ConversationDetail> SafeConversationDetails => ConversationDetails ?? [];
    public IReadOnlyList<OfflineQueuedEdit> SafeQueuedEdits => QueuedEdits ?? [];
}

public sealed record RunRecoveryState(string RunId, string ConversationId, long LastSequence, DateTimeOffset SavedAt);
