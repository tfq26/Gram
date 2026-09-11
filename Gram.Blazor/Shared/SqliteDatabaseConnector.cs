using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Gram.Blazor.Shared;

public sealed class SqliteDatabaseConnector : IConnectedConnector
{
    public ConnectorDescriptor Descriptor => new(
        "db-sqlite",
        "SQLite Database",
        ConnectorTransport.Native,
        "Safely inspect schemas and query SQLite databases with enforced read-only jails and monitored execution.",
        RequiresConnection: false);

    public IReadOnlyList<ConnectorAction> Actions =>
    [
        new("list_tables", "db-sqlite", "List tables", "List user tables and views in the SQLite database.", ActionRiskLevel.Safe),
        new("inspect_schema", "db-sqlite", "Inspect schema", "Inspect table column names, types, nullability, and primary keys.", ActionRiskLevel.Safe),
        new("query_readonly", "db-sqlite", "Execute read-only query", "Execute a safe SELECT query within an enforced read-only jail.", ActionRiskLevel.Safe),
        new("execute_dml", "db-sqlite", "Execute mutating SQL", "Execute an approved INSERT, UPDATE, DELETE, or DDL statement.", ActionRiskLevel.Sensitive, RequiresApproval: true)
    ];

    public async Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default)
    {
        using var document = request.Parameters.ValueKind == JsonValueKind.Undefined
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(request.Parameters.GetRawText());
        var root = document.RootElement;

        var connectionStringOrPath = root.TryGetProperty("connectionStringOrPath", out var csProp)
            ? csProp.GetString()
            : root.TryGetProperty("path", out var pProp)
                ? pProp.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(connectionStringOrPath))
        {
            return new ConnectorActionResult(false, "invalid_parameters", "A SQLite database path or connection string is required.");
        }

        try
        {
            return request.ActionId.ToLowerInvariant() switch
            {
                "list_tables" => await ListTablesAsync(connectionStringOrPath, cancellationToken),
                "inspect_schema" => await InspectSchemaAsync(connectionStringOrPath, root, cancellationToken),
                "query_readonly" => await QueryReadOnlyAsync(connectionStringOrPath, root, cancellationToken),
                "execute_dml" => await ExecuteDmlAsync(connectionStringOrPath, root, cancellationToken),
                _ => new ConnectorActionResult(false, "action_not_found", $"Unknown action '{request.ActionId}'.")
            };
        }
        catch (OperationCanceledException)
        {
            return new ConnectorActionResult(false, "timeout", "The database operation timed out or was cancelled.");
        }
        catch (Exception ex)
        {
            return new ConnectorActionResult(false, "execution_error", ex.Message);
        }
    }

    private static async Task<ConnectorActionResult> ListTablesAsync(string connectionStringOrPath, CancellationToken cancellationToken)
    {
        var cs = BuildConnectionString(connectionStringOrPath, readOnly: true);
        using var connection = new SqliteConnection(cs);
        await connection.OpenAsync(cancellationToken);

        const string sql = "SELECT name, type FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        var tables = new List<object>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(new
            {
                name = reader.GetString(0),
                type = reader.GetString(1)
            });
        }

        return new ConnectorActionResult(
            true,
            "completed",
            Data: JsonSerializer.SerializeToElement(new { tables, count = tables.Count }));
    }

    private static async Task<ConnectorActionResult> InspectSchemaAsync(string connectionStringOrPath, JsonElement root, CancellationToken cancellationToken)
    {
        var tableName = root.TryGetProperty("tableName", out var tProp)
            ? tProp.GetString()
            : root.TryGetProperty("table", out var t2) ? t2.GetString() : null;

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return new ConnectorActionResult(false, "invalid_parameters", "A 'tableName' parameter is required.");
        }

        var cs = BuildConnectionString(connectionStringOrPath, readOnly: true);
        using var connection = new SqliteConnection(cs);
        await connection.OpenAsync(cancellationToken);

        // Sanitize table name to prevent injection into PRAGMA
        var cleanTableName = tableName.Replace("'", "''");
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{cleanTableName}');";

        var columns = new List<ColumnSchemaInfo>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // cid, name, type, notnull, dflt_value, pk
            var colName = reader.GetString(1);
            var colType = reader.IsDBNull(2) ? "TEXT" : reader.GetString(2);
            var notNull = reader.GetInt32(3) == 1;
            var defaultVal = reader.IsDBNull(4) ? null : reader.GetString(4);
            var isPk = reader.GetInt32(5) >= 1;

            columns.Add(new ColumnSchemaInfo(colName, colType, !notNull, isPk, defaultVal));
        }

        var schemaInfo = new TableSchemaInfo(tableName, "table", columns);
        return new ConnectorActionResult(
            true,
            "completed",
            Data: JsonSerializer.SerializeToElement(schemaInfo));
    }

    private static async Task<ConnectorActionResult> QueryReadOnlyAsync(string connectionStringOrPath, JsonElement root, CancellationToken cancellationToken)
    {
        var sql = root.TryGetProperty("query", out var qProp)
            ? qProp.GetString()
            : root.TryGetProperty("sql", out var sProp) ? sProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(sql))
        {
            return new ConnectorActionResult(false, "invalid_parameters", "A 'query' parameter is required.");
        }

        var classification = SafeSqlSanitizer.ClassifyAndSanitize(sql, DatabaseEngine.Sqlite);
        if (!classification.IsReadOnly || classification.Error != null)
        {
            return new ConnectorActionResult(
                false,
                "safety_violation",
                classification.Error ?? "Only read-only SELECT or EXPLAIN statements are permitted in query_readonly.");
        }

        var cs = BuildConnectionString(connectionStringOrPath, readOnly: true);
        using var connection = new SqliteConnection(cs);
        await connection.OpenAsync(cancellationToken);

        // Enforce engine-level read-only jail
        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA query_only = ON;";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = classification.SanitizedSql;
        cmd.CommandTimeout = 5; // 5-second hard limit

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        var sw = Stopwatch.StartNew();
        using var reader = await cmd.ExecuteReaderAsync(timeoutCts.Token);

        var columns = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var totalRows = 0;
        var truncated = false;

        while (await reader.ReadAsync(timeoutCts.Token))
        {
            totalRows++;
            if (rows.Count < 500)
            {
                var row = new Dictionary<string, object?>(columns.Count, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }
            else
            {
                truncated = true;
            }
        }
        sw.Stop();

        var redactedRows = SafeSqlSanitizer.RedactSensitiveRows(columns, rows);
        var result = new DatabaseQueryResult(
            Success: true,
            Columns: columns,
            Rows: redactedRows,
            TotalRows: totalRows,
            ExecutionMs: sw.ElapsedMilliseconds,
            Truncated: truncated);

        return new ConnectorActionResult(
            true,
            "completed",
            Data: JsonSerializer.SerializeToElement(result));
    }

    private static async Task<ConnectorActionResult> ExecuteDmlAsync(string connectionStringOrPath, JsonElement root, CancellationToken cancellationToken)
    {
        var sql = root.TryGetProperty("sql", out var sProp)
            ? sProp.GetString()
            : root.TryGetProperty("query", out var qProp) ? qProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(sql))
        {
            return new ConnectorActionResult(false, "invalid_parameters", "A 'sql' statement parameter is required.");
        }

        var classification = SafeSqlSanitizer.ClassifyAndSanitize(sql, DatabaseEngine.Sqlite);
        if (classification.Error != null || classification.Type == SqlStatementType.Prohibited)
        {
            return new ConnectorActionResult(
                false,
                "safety_violation",
                classification.Error ?? "The specified SQL statement is prohibited.");
        }

        var cs = BuildConnectionString(connectionStringOrPath, readOnly: false);
        using var connection = new SqliteConnection(cs);
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = classification.SanitizedSql;
        cmd.CommandTimeout = 10;

        var sw = Stopwatch.StartNew();
        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        sw.Stop();

        return new ConnectorActionResult(
            true,
            "completed",
            Data: JsonSerializer.SerializeToElement(new
            {
                rowsAffected = affected,
                statementType = classification.Type.ToString(),
                executionMs = sw.ElapsedMilliseconds
            }));
    }

    private static string BuildConnectionString(string connectionStringOrPath, bool readOnly)
    {
        if (connectionStringOrPath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new SqliteConnectionStringBuilder(connectionStringOrPath)
            {
                Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate
            };
            return builder.ToString();
        }

        return new SqliteConnectionStringBuilder
        {
            DataSource = connectionStringOrPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }
}
