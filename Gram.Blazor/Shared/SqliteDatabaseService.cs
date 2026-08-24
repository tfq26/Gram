using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Gram.Blazor.Shared;

public sealed class UserRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Role { get; set; } = "user";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class UserSessionRecord
{
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(30);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SqliteDatabaseService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public SqliteDatabaseService(StorageService storageService)
    {
        _dbPath = storageService.GetFilePath("gram_auth.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY,
                email TEXT UNIQUE NOT NULL,
                name TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                salt TEXT NOT NULL,
                role TEXT NOT NULL DEFAULT 'user',
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sessions (
                token TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_sessions_user_id ON sessions(user_id);
            CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
        ";
        cmd.ExecuteNonQuery();

        // Seed default admin user if database is empty
        SeedDefaultAdmin(conn);
    }

    private void SeedDefaultAdmin(SqliteConnection conn)
    {
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM users;";
        var count = Convert.ToInt64(checkCmd.ExecuteScalar());

        if (count == 0)
        {
            var salt = GenerateSalt();
            var hash = HashPassword("admin123", salt);
            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO users (id, email, name, password_hash, salt, role, created_at)
                VALUES ('admin-001', 'admin@gram.local', 'Admin User', @hash, @salt, 'admin', @createdAt);
            ";
            insertCmd.Parameters.AddWithValue("@hash", hash);
            insertCmd.Parameters.AddWithValue("@salt", salt);
            insertCmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));
            insertCmd.ExecuteNonQuery();
        }
    }

    public async Task<UserRecord?> GetUserByEmailAsync(string email)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, email, name, password_hash, salt, role, created_at FROM users WHERE LOWER(email) = LOWER(@email);";
        cmd.Parameters.AddWithValue("@email", email.Trim());

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new UserRecord
            {
                Id = reader.GetString(0),
                Email = reader.GetString(1),
                Name = reader.GetString(2),
                PasswordHash = reader.GetString(3),
                Salt = reader.GetString(4),
                Role = reader.GetString(5),
                CreatedAt = DateTime.Parse(reader.GetString(6))
            };
        }
        return null;
    }

    public async Task<UserRecord?> GetUserByIdAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, email, name, password_hash, salt, role, created_at FROM users WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new UserRecord
            {
                Id = reader.GetString(0),
                Email = reader.GetString(1),
                Name = reader.GetString(2),
                PasswordHash = reader.GetString(3),
                Salt = reader.GetString(4),
                Role = reader.GetString(5),
                CreatedAt = DateTime.Parse(reader.GetString(6))
            };
        }
        return null;
    }

    public async Task<UserRecord> CreateUserAsync(string email, string name, string password, string role = "user")
    {
        var salt = GenerateSalt();
        var hash = HashPassword(password, salt);
        var record = new UserRecord
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Email = email.Trim().ToLowerInvariant(),
            Name = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name.Trim(),
            PasswordHash = hash,
            Salt = salt,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (id, email, name, password_hash, salt, role, created_at)
            VALUES (@id, @email, @name, @hash, @salt, @role, @createdAt);
        ";
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@email", record.Email);
        cmd.Parameters.AddWithValue("@name", record.Name);
        cmd.Parameters.AddWithValue("@hash", record.PasswordHash);
        cmd.Parameters.AddWithValue("@salt", record.Salt);
        cmd.Parameters.AddWithValue("@role", record.Role);
        cmd.Parameters.AddWithValue("@createdAt", record.CreatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
        return record;
    }

    public async Task<UserSessionRecord> CreateSessionAsync(string userId)
    {
        var session = new UserSessionRecord
        {
            Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        };

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sessions (token, user_id, expires_at, created_at)
            VALUES (@token, @userId, @expiresAt, @createdAt);
        ";
        cmd.Parameters.AddWithValue("@token", session.Token);
        cmd.Parameters.AddWithValue("@userId", session.UserId);
        cmd.Parameters.AddWithValue("@expiresAt", session.ExpiresAt.ToString("o"));
        cmd.Parameters.AddWithValue("@createdAt", session.CreatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
        return session;
    }

    public async Task<UserRecord?> ValidateSessionAsync(string token)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT u.id, u.email, u.name, u.password_hash, u.salt, u.role, u.created_at, s.expires_at
            FROM sessions s
            JOIN users u ON s.user_id = u.id
            WHERE s.token = @token;
        ";
        cmd.Parameters.AddWithValue("@token", token);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var expiresAt = DateTime.Parse(reader.GetString(7));
            if (expiresAt < DateTime.UtcNow)
            {
                // Expired
                return null;
            }

            return new UserRecord
            {
                Id = reader.GetString(0),
                Email = reader.GetString(1),
                Name = reader.GetString(2),
                PasswordHash = reader.GetString(3),
                Salt = reader.GetString(4),
                Role = reader.GetString(5),
                CreatedAt = DateTime.Parse(reader.GetString(6))
            };
        }
        return null;
    }

    public async Task InvalidateSessionAsync(string token)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sessions WHERE token = @token;";
        cmd.Parameters.AddWithValue("@token", token);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> UpdateUserProfileAsync(string userId, string name)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET name = @name WHERE id = @id;";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.Parameters.AddWithValue("@id", userId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<bool> UpdatePasswordAsync(string userId, string newPassword)
    {
        var salt = GenerateSalt();
        var hash = HashPassword(newPassword, salt);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET password_hash = @hash, salt = @salt WHERE id = @id;";
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@salt", salt);
        cmd.Parameters.AddWithValue("@id", userId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public static bool VerifyPassword(string enteredPassword, string storedHash, string salt)
    {
        var computed = HashPassword(enteredPassword, salt);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(storedHash));
    }

    private static string GenerateSalt()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashPassword(string password, string salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), 100_000, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(pbkdf2.GetBytes(32));
    }
}
