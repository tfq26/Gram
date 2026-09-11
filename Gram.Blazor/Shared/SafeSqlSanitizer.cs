using System.Text;
using System.Text.RegularExpressions;

namespace Gram.Blazor.Shared;

public static class SafeSqlSanitizer
{
    private static readonly Regex SensitiveColumnPattern = new(
        @"(password|passwd|pwd|secret|token|hash|salt|api_?key|private_?key|auth_?token|client_?secret|access_?token|refresh_?token)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ProhibitedCommandPattern = new(
        @"\b(ATTACH\s+(?:DATABASE\s+)?|DETACH\s+(?:DATABASE\s+)?|load_extension|PRAGMA\s+writable_schema|COPY\s+[\s\S]*?\s+PROGRAM|pg_read_file|pg_write_file|pg_read_binary_file|pg_terminate_backend|pg_cancel_backend|ALTER\s+SYSTEM|CREATE\s+EXTENSION|DROP\s+EXTENSION|GRANT\s+|REVOKE\s+|xp_cmdshell|SHUTDOWN)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LimitClausePattern = new(
        @"\bLIMIT\s+(\d+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AllowedPragmaPattern = new(
        @"^PRAGMA\s+(?:main\.|temp\.)?(table_info|table_xinfo|index_list|foreign_key_list|database_list|collation_list|schema_version)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static SqlStatementClassification ClassifyAndSanitize(
        string rawSql,
        DatabaseEngine engine,
        int defaultLimit = 100,
        int maxLimit = 1000)
    {
        if (string.IsNullOrWhiteSpace(rawSql))
        {
            return new SqlStatementClassification(
                SqlStatementType.Prohibited,
                IsReadOnly: false,
                IsMutating: false,
                SanitizedSql: "",
                Error: "SQL query cannot be empty.");
        }

        var stripped = StripComments(rawSql).Trim();
        if (string.IsNullOrWhiteSpace(stripped))
        {
            return new SqlStatementClassification(
                SqlStatementType.Prohibited,
                IsReadOnly: false,
                IsMutating: false,
                SanitizedSql: "",
                Error: "SQL query contains no executable statements.");
        }

        // 1. Enforce single-statement execution (reject stacked queries)
        if (ContainsMultipleStatements(stripped))
        {
            return new SqlStatementClassification(
                SqlStatementType.Prohibited,
                IsReadOnly: false,
                IsMutating: false,
                SanitizedSql: stripped,
                Error: "Multi-statement SQL queries are strictly prohibited for safety.");
        }

        // 2. Check prohibited administrative / OS commands
        if (ProhibitedCommandPattern.IsMatch(stripped))
        {
            var match = ProhibitedCommandPattern.Match(stripped);
            return new SqlStatementClassification(
                SqlStatementType.Prohibited,
                IsReadOnly: false,
                IsMutating: false,
                SanitizedSql: stripped,
                Error: $"Prohibited administrative or system operation detected: '{match.Value.Trim()}'.");
        }

        // 3. Identify statement type
        var type = DetermineStatementType(stripped, engine);
        var isReadOnly = type is SqlStatementType.Select or SqlStatementType.Explain or SqlStatementType.PragmaRead;
        var isMutating = type is SqlStatementType.Insert or SqlStatementType.Update or SqlStatementType.Delete or
                         SqlStatementType.Create or SqlStatementType.Alter or SqlStatementType.Drop or
                         SqlStatementType.Truncate or SqlStatementType.OtherDml;

        if (type == SqlStatementType.Prohibited)
        {
            return new SqlStatementClassification(
                SqlStatementType.Prohibited,
                IsReadOnly: false,
                IsMutating: false,
                SanitizedSql: stripped,
                Error: "The statement type is not permitted or recognized as safe.");
        }

        // 4. Inject or clamp LIMIT for SELECT statements
        var finalSql = stripped;
        if (type == SqlStatementType.Select)
        {
            finalSql = EnforceLimit(stripped, defaultLimit, maxLimit);
        }

        return new SqlStatementClassification(
            type,
            IsReadOnly: isReadOnly,
            IsMutating: isMutating,
            SanitizedSql: finalSql);
    }

    public static string StripComments(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return "";

        var sb = new StringBuilder(sql.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            // Handle string literal boundaries
            if (c == '\'' && !inDoubleQuote)
            {
                // Escaped single quote ('')
                if (inSingleQuote && next == '\'')
                {
                    sb.Append("''");
                    i++;
                    continue;
                }
                inSingleQuote = !inSingleQuote;
                sb.Append(c);
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                sb.Append(c);
                continue;
            }

            // Outside quotes, check for comments
            if (!inSingleQuote && !inDoubleQuote)
            {
                // Line comment: --
                if (c == '-' && next == '-')
                {
                    // Skip until newline
                    i += 2;
                    while (i < sql.Length && sql[i] != '\n' && sql[i] != '\r') i++;
                    if (i < sql.Length) sb.Append(sql[i]);
                    continue;
                }

                // Block comment: /* ... */
                if (c == '/' && next == '*')
                {
                    i += 2;
                    while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                    i++; // skip '/'
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    public static bool ContainsMultipleStatements(string sql)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var seenSemicolon = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (c == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && next == '\'')
                {
                    i++;
                    continue;
                }
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote)
            {
                if (c == ';')
                {
                    seenSemicolon = true;
                }
                else if (seenSemicolon && !char.IsWhiteSpace(c))
                {
                    // Found a non-whitespace character after a semicolon outside quotes -> multi-statement
                    return true;
                }
            }
        }

        return false;
    }

    public static string EnforceLimit(string selectSql, int defaultLimit = 100, int maxLimit = 1000)
    {
        var clean = selectSql.Trim();
        var hasTrailingSemicolon = clean.EndsWith(';');
        if (hasTrailingSemicolon)
        {
            clean = clean[..^1].TrimEnd();
        }

        var limitMatch = LimitClausePattern.Match(clean);
        if (limitMatch.Success)
        {
            if (int.TryParse(limitMatch.Groups[1].Value, out var existingLimit))
            {
                if (existingLimit > maxLimit)
                {
                    var clamped = LimitClausePattern.Replace(clean, $"LIMIT {maxLimit}");
                    return hasTrailingSemicolon ? $"{clamped};" : clamped;
                }
            }
            return selectSql;
        }

        // No limit clause exists -> append default limit
        var withLimit = $"{clean}\nLIMIT {defaultLimit}";
        return hasTrailingSemicolon ? $"{withLimit};" : withLimit;
    }

    public static bool IsSensitiveColumn(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return false;
        return SensitiveColumnPattern.IsMatch(columnName.Trim());
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> RedactSensitiveRows(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var sensitiveCols = columns.Where(IsSensitiveColumn).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sensitiveCols.Count == 0) return rows;

        var sanitized = new List<IReadOnlyDictionary<string, object?>>(rows.Count);
        foreach (var row in rows)
        {
            var newRow = new Dictionary<string, object?>(row.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in row)
            {
                newRow[k] = sensitiveCols.Contains(k) && v != null ? "[REDACTED]" : v;
            }
            sanitized.Add(newRow);
        }

        return sanitized;
    }

    private static SqlStatementType DetermineStatementType(string sql, DatabaseEngine engine)
    {
        var trimmed = sql.TrimStart();

        // Check for Common Table Expressions (WITH ...)
        if (trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            // Find what statement follows the CTE definition
            if (Regex.IsMatch(trimmed, @"\)\s*SELECT\b", RegexOptions.IgnoreCase))
            {
                return SqlStatementType.Select;
            }
            if (Regex.IsMatch(trimmed, @"\)\s*INSERT\b", RegexOptions.IgnoreCase))
            {
                return SqlStatementType.Insert;
            }
            if (Regex.IsMatch(trimmed, @"\)\s*UPDATE\b", RegexOptions.IgnoreCase))
            {
                return SqlStatementType.Update;
            }
            if (Regex.IsMatch(trimmed, @"\)\s*DELETE\b", RegexOptions.IgnoreCase))
            {
                return SqlStatementType.Delete;
            }
        }

        var firstWord = GetFirstWord(trimmed);

        return firstWord.ToUpperInvariant() switch
        {
            "SELECT" => SqlStatementType.Select,
            "EXPLAIN" => SqlStatementType.Explain,
            "PRAGMA" when engine == DatabaseEngine.Sqlite && AllowedPragmaPattern.IsMatch(trimmed) => SqlStatementType.PragmaRead,
            "INSERT" => SqlStatementType.Insert,
            "UPDATE" => SqlStatementType.Update,
            "DELETE" => SqlStatementType.Delete,
            "CREATE" => SqlStatementType.Create,
            "ALTER" => SqlStatementType.Alter,
            "DROP" => SqlStatementType.Drop,
            "TRUNCATE" => SqlStatementType.Truncate,
            "REINDEX" or "VACUUM" or "MERGE" => SqlStatementType.OtherDml,
            _ => SqlStatementType.Prohibited
        };
    }

    private static string GetFirstWord(string text)
    {
        var i = 0;
        while (i < text.Length && !char.IsLetterOrDigit(text[i]) && text[i] != '_') i++;
        var start = i;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
        return i > start ? text[start..i] : "";
    }
}
