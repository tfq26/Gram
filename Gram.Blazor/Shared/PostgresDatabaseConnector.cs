using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Npgsql;

namespace Gram.Blazor.Shared;

public sealed class PostgresDatabaseConnector : IConnectedConnector
{
    private readonly IConnectorCredentialVault? _vault;

    public PostgresDatabaseConnector(IConnectorCredentialVault? vault = null)
    {
        _vault = vault;
    }

    public ConnectorDescriptor Descriptor => new(
        "db-postgres",
        "PostgreSQL Database",
        ConnectorTransport.Native,
        "Safely inspect schemas and query PostgreSQL databases with enforced read-only transactions and monitored execution.",
        RequiresConnection: true);

    public IReadOnlyList<ConnectorAction> Actions =>
    [
        new("list_tables", "db-postgres", "List tables", "List user schemas, tables, and views in the connected PostgreSQL database.", ActionRiskLevel.Safe),
        new("inspect_schema", "db-postgres", "Inspect schema", "Inspect table column names, types, nullability, and defaults.", ActionRiskLevel.Safe),
        new("query_readonly", "db-postgres", "Execute read-only query", "Execute a safe SELECT query within an enforced read-only transaction.", ActionRiskLevel.Safe),
        new("execute_dml", "db-postgres", "Execute mutating SQL", "Execute an approved INSERT, UPDATE, DELETE, or DDL statement.", ActionRiskLevel.Sensitive, RequiresApproval: true)
    ];

    public async Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default)
    {
        using var document = request.Parameters.ValueKind == JsonValueKind.Undefined
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(request.Parameters.GetRawText());
        var root = document.RootElement;

        var connectionString = root.TryGetProperty("connectionString", out var csProp)
            ? csProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(connectionString) && _vault != null && !string.IsNullOrWhiteSpace(request.UserId) && !string.IsNullOrWhiteSpace(request.ConnectionId))
        {
            connectionString = await _vault.ResolveAsync(
                new ConnectorCredentialReference(request.UserId, "db-postgres", request.ConnectionId, "connection_string"),
                ["connection_string"],
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new ConnectorActionResult(false, "connection_required", "A PostgreSQL connection string is required or must be configured.");
        }

        try
        {
            return request.ActionId.ToLowerInvariant() switch
            {
                "list_tables" => await ListTablesAsync(connectionString, cancellationToken),
                "inspect_schema" => await InspectSchemaAsync(connectionString, root, cancellationToken),
                "query_readonly" => await QueryReadOnlyAsync(connectionString, root, cancellationToken),
                "execute_dml" => await ExecuteDmlAsync(connectionString, root, cancellationToken),
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

    private static async Task<ConnectorActionResult> ListTablesAsync(string connectionString, CancellationToken cancellationToken)
    {
        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = """
            SELECT table_schema, table_name, table_type 
            FROM information_schema.tables 
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema') 
            ORDER BY table_schema, table_name;
            """;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var tables = new List<object>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(new
            {
                schema = reader.GetString(0),
                name = reader.GetString(1),
                type = reader.GetString(2)
            });
        }

        return new ConnectorActionResult(
            true,
            "completed",
            Data: JsonSerializer.SerializeToElement(new { tables, count = tables.Count }));
    }

    private static async Task<ConnectorActionResult> InspectSchemaAsync(string connectionString, JsonElement root, CancellationToken cancellationToken)
    {
        var tableName = root.TryGetProperty("tableName", out var tProp)
            ? tProp.GetString()
            : root.TryGetProperty("table", out var t2) ? t2.GetString() : null;

        var schemaName = root.TryGetProperty("schema", out var sProp) ? sProp.GetString() : "public";

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return new ConnectorActionResult(false, "invalid_parameters", "A 'tableName' parameter is required.");
        }

        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = """
            SELECT c.column_name, c.data_type, c.is_nullable, c.column_default,
                   CASE WHEN pk.column_name IS NOT NULL THEN true ELSE false END as is_pk
            FROM information_schema.columns c
            LEFT JOIN (
                SELECT kcu.column_name, kcu.table_schema, kcu.table_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON tc.constraint_name = kcu.constraint_name
                 AND tc.table_schema = kcu.table_schema
                WHERE tc.constraint_type = 'PRIMARY KEY'
            ) pk ON pk.table_schema = c.table_schema AND pk.table_name = c.table_name AND pk.column_name = c.column_name
            WHERE c.table_name = @tbl
              AND (c.table_schema = @sch OR @sch IS NULL)
            ORDER BY c.ordinal_position;
            """;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("tbl", tableName);
        cmd.Parameters.AddWithValue("sch", (object?)schemaName ?? DBNull.Value);

        var columns = new List<ColumnSchemaInfo>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var colName = reader.GetString(0);
            var dataType = reader.GetString(1);
            var isNullable = reader.GetString(2).Equals("YES", StringComparison.OrdinalIgnoreCase);
            var defaultVal = reader.IsDBNull(3) ? null : reader.GetString(3);
            var isPk = reader.GetBoolean(4);

            columns.Add(new ColumnSchemaInfo(colName, dataType, isNullable, isPk, defaultVal));
        }

        var schemaInfo = new TableSchemaInfo(tableName, "table", columns);
        return new ConnectorActionResult(
            true,
            "completed",
            Data: JsonSerializer.SerializeToElement(schemaInfo));
    }

    private static async Task<ConnectorActionResult> QueryReadOnlyAsync(string connectionString, JsonElement root, CancellationToken cancellationToken)
    {
        var sql = root.TryGetProperty("query", out var qProp)
            ? qProp.GetString()
            : root.TryGetProperty("sql", out var sProp) ? sProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(sql))
        {
            return new ConnectorActionResult(false, "invalid_parameters", "A 'query' parameter is required.");
        }

        var classification = SafeSqlSanitizer.ClassifyAndSanitize(sql, DatabaseEngine.Postgres);
        if (!classification.IsReadOnly || classification.Error != null)
        {
            return new ConnectorActionResult(
                false,
                "safety_violation",
                classification.Error ?? "Only read-only SELECT or EXPLAIN statements are permitted in query_readonly.");
        }

        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        // Enforce engine-level read-only transaction jail
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        using (var setCmd = conn.CreateCommand())
        {
            setCmd.Transaction = tx;
            setCmd.CommandText = "SET TRANSACTION READ ONLY;";
            await setCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
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

        await tx.CommitAsync(cancellationToken);

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

    private static async Task<ConnectorActionResult> ExecuteDmlAsync(string connectionString, JsonElement root, CancellationToken cancellationToken)
    {
        var sql = root.TryGetProperty("sql", out var sProp)
            ? sProp.GetString()
            : root.TryGetProperty("query", out var qProp) ? qProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(sql))
        {
            return new ConnectorActionResult(false, "invalid_parameters", "A 'sql' statement parameter is required.");
        }

        var classification = SafeSqlSanitizer.ClassifyAndSanitize(sql, DatabaseEngine.Postgres);
        if (classification.Error != null || classification.Type == SqlStatementType.Prohibited)
        {
            return new ConnectorActionResult(
                false,
                "safety_violation",
                classification.Error ?? "The specified SQL statement is prohibited.");
        }

        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = classification.SanitizedSql;
        cmd.CommandTimeout = 10;

        var sw = Stopwatch.StartNew();
        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
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
}
