using System.Text.Json;
using Gram.Blazor.Shared;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SqliteConnectorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDatabaseConnector _connector;

    public SqliteConnectorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gram_sqlite_test_{Guid.NewGuid():N}.db");
        _connector = new SqliteDatabaseConnector();

        // Seed schema and data
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                email TEXT
            );
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                total REAL NOT NULL
            );
            CREATE VIEW active_users AS SELECT id, username FROM users;

            INSERT INTO users (username, password_hash, email) VALUES 
            ('alice', '$2a$12$e8r98234...', 'alice@example.com'),
            ('bob', '$2a$12$e8r98235...', 'bob@example.com');
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task ListTablesReturnsTablesAndViews()
    {
        var parameters = JsonSerializer.SerializeToElement(new { path = _dbPath });
        var request = new ConnectorActionRequest("db-sqlite", "list_tables", parameters);

        var result = await _connector.ExecuteAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var json = result.Data.Value.GetRawText();
        Assert.Contains("users", json);
        Assert.Contains("orders", json);
        Assert.Contains("active_users", json);
    }

    [Fact]
    public async Task InspectSchemaReturnsColumnMetadata()
    {
        var parameters = JsonSerializer.SerializeToElement(new { path = _dbPath, tableName = "users" });
        var request = new ConnectorActionRequest("db-sqlite", "inspect_schema", parameters);

        var result = await _connector.ExecuteAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var schema = JsonSerializer.Deserialize<TableSchemaInfo>(result.Data.Value.GetRawText());
        Assert.NotNull(schema);
        Assert.Equal("users", schema!.TableName);
        Assert.Contains(schema.Columns, c => c.ColumnName == "id" && c.IsPrimaryKey);
        Assert.Contains(schema.Columns, c => c.ColumnName == "username" && !c.IsNullable);
        Assert.Contains(schema.Columns, c => c.ColumnName == "password_hash");
    }

    [Fact]
    public async Task QueryReadOnlyExecutesSelectAndRedactsSecrets()
    {
        var parameters = JsonSerializer.SerializeToElement(new { path = _dbPath, query = "SELECT id, username, password_hash, email FROM users;" });
        var request = new ConnectorActionRequest("db-sqlite", "query_readonly", parameters);

        var result = await _connector.ExecuteAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var queryResult = JsonSerializer.Deserialize<DatabaseQueryResult>(result.Data.Value.GetRawText());
        Assert.NotNull(queryResult);
        Assert.True(queryResult!.Success);
        Assert.Equal(2, queryResult.TotalRows);

        // Password hash must be redacted
        Assert.Equal("[REDACTED]", queryResult.Rows[0]["password_hash"]?.ToString());
        Assert.Equal("alice", queryResult.Rows[0]["username"]?.ToString());
    }

    [Fact]
    public async Task QueryReadOnlyRejectsMutatingInsertStatement()
    {
        var parameters = JsonSerializer.SerializeToElement(new { path = _dbPath, query = "INSERT INTO users (username, password_hash) VALUES ('charlie', 'secret');" });
        var request = new ConnectorActionRequest("db-sqlite", "query_readonly", parameters);

        var result = await _connector.ExecuteAsync(request);

        Assert.False(result.Success);
        Assert.Equal("safety_violation", result.Status);
    }

    [Fact]
    public async Task ExecuteDmlModifiesData()
    {
        var parameters = JsonSerializer.SerializeToElement(new { path = _dbPath, sql = "INSERT INTO users (username, password_hash, email) VALUES ('charlie', 'hash123', 'charlie@example.com');" });
        var request = new ConnectorActionRequest("db-sqlite", "execute_dml", parameters);

        var result = await _connector.ExecuteAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains("\"rowsAffected\":1", result.Data.Value.GetRawText());
    }
}
