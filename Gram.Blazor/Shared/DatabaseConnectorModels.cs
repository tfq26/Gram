using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public enum DatabaseEngine
{
    Sqlite,
    Postgres
}

public enum SqlStatementType
{
    Select,
    Explain,
    PragmaRead,
    Insert,
    Update,
    Delete,
    Create,
    Alter,
    Drop,
    Truncate,
    OtherDml,
    Prohibited
}

public sealed record DatabaseProfile(
    string Id,
    DatabaseEngine Engine,
    string DisplayName,
    string ConnectionStringOrPath,
    bool IsReadOnlyDefault = true,
    DateTimeOffset CreatedAt = default
);

public sealed record ColumnSchemaInfo(
    string ColumnName,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey,
    string? DefaultValue = null
);

public sealed record TableSchemaInfo(
    string TableName,
    string TableType, // "table", "view"
    IReadOnlyList<ColumnSchemaInfo> Columns
);

public sealed record DatabaseQueryResult(
    bool Success,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int TotalRows,
    long ExecutionMs,
    string? Error = null,
    bool Truncated = false
);

public sealed record SqlStatementClassification(
    SqlStatementType Type,
    bool IsReadOnly,
    bool IsMutating,
    string SanitizedSql,
    string? Error = null
);
