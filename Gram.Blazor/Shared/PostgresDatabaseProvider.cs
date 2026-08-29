using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Gram.Blazor.Shared;

public sealed class PostgresDatabaseProvider : IDatabaseProvider
{
    private readonly ILogger<PostgresDatabaseProvider> _logger;
    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;

    public string ProviderName => "PostgreSQL (Cloud / Railway)";

    public PostgresDatabaseProvider(ILogger<PostgresDatabaseProvider> logger, string connectionStringOrUrl)
    {
        _logger = logger;
        _connectionString = FormatConnectionString(connectionStringOrUrl);

        var builder = new NpgsqlDataSourceBuilder(_connectionString);
        _dataSource = builder.Build();
    }

    private static string FormatConnectionString(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        // Support Railway / Heroku style postgres:// or postgresql:// URLs
        if (input.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || 
            input.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(input);
            var userInfo = uri.UserInfo.Split(':');
            var user = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
            var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var port = uri.Port > 0 ? uri.Port : 5432;
            var database = uri.AbsolutePath.TrimStart('/');

            var cb = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = port,
                Database = database,
                Username = user,
                Password = pass,
                SslMode = SslMode.Prefer,
                Pooling = true,
                MaxPoolSize = 20
            };
            return cb.ConnectionString;
        }

        return input;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS users (
                    id VARCHAR(64) PRIMARY KEY,
                    email VARCHAR(255) UNIQUE NOT NULL,
                    name VARCHAR(255) NOT NULL,
                    password_hash TEXT NOT NULL,
                    salt TEXT NOT NULL,
                    role VARCHAR(32) NOT NULL DEFAULT 'user',
                    image TEXT,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );

                CREATE TABLE IF NOT EXISTS sessions (
                    token VARCHAR(128) PRIMARY KEY,
                    user_id VARCHAR(64) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                    expires_at TIMESTAMPTZ NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );

                CREATE TABLE IF NOT EXISTS conversations (
                    id VARCHAR(64) PRIMARY KEY,
                    user_id VARCHAR(64) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                    title VARCHAR(255) NOT NULL,
                    model_id VARCHAR(128),
                    system_prompt TEXT,
                    rules TEXT,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    data_json JSONB NOT NULL,
                    version INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE IF NOT EXISTS user_passkeys (
                    id VARCHAR(255) PRIMARY KEY,
                    user_id VARCHAR(64) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                    public_key TEXT NOT NULL,
                    counter BIGINT NOT NULL DEFAULT 0,
                    name VARCHAR(255) NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );

                CREATE TABLE IF NOT EXISTS password_reset_tokens (
                    id VARCHAR(64) PRIMARY KEY,
                    user_id VARCHAR(64) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                    code VARCHAR(16) NOT NULL,
                    token VARCHAR(128) NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    expires_at TIMESTAMPTZ NOT NULL,
                    used BOOLEAN NOT NULL DEFAULT FALSE
                );

                CREATE INDEX IF NOT EXISTS idx_sessions_user_id ON sessions(user_id);
                CREATE INDEX IF NOT EXISTS idx_conversations_user_id ON conversations(user_id);
                CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
                CREATE INDEX IF NOT EXISTS idx_user_passkeys_user_id ON user_passkeys(user_id);
                CREATE INDEX IF NOT EXISTS idx_reset_user_id ON password_reset_tokens(user_id);
                CREATE INDEX IF NOT EXISTS idx_reset_code ON password_reset_tokens(code);
                CREATE INDEX IF NOT EXISTS idx_reset_token ON password_reset_tokens(token);

                ALTER TABLE users ADD COLUMN IF NOT EXISTS image TEXT;
                ALTER TABLE conversations ADD COLUMN IF NOT EXISTS version INTEGER NOT NULL DEFAULT 1;
            """;
            await cmd.ExecuteNonQueryAsync(ct);

            // Ensure no default admin account exists in Gram Cloud (admin account is Mini-Gram only)
            await using var cleanupCmd = conn.CreateCommand();
            cleanupCmd.CommandText = "DELETE FROM users WHERE email = 'admin@gram.local' OR id = 'admin-001';";
            await cleanupCmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation("PostgresDatabaseProvider: Database schema initialized successfully (no default admin accounts).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Postgres database schema");
            throw;
        }
    }

    // Default admin seeding is disabled for Gram Cloud (web app) — reserved for Mini-Gram offline only.

    public async Task<UserRecord?> GetUserByEmailAsync(string email)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, email, name, password_hash, salt, role, created_at, image FROM users WHERE LOWER(email) = LOWER(@email);";
        cmd.Parameters.AddWithValue("@email", email.Trim());

        await using var reader = await cmd.ExecuteReaderAsync();
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
                CreatedAt = reader.GetDateTime(6),
                Image = reader.IsDBNull(7) ? null : reader.GetString(7)
            };
        }
        return null;
    }

    public async Task<UserRecord?> GetUserByIdAsync(string id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, email, name, password_hash, salt, role, created_at, image FROM users WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
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
                CreatedAt = reader.GetDateTime(6),
                Image = reader.IsDBNull(7) ? null : reader.GetString(7)
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

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users (id, email, name, password_hash, salt, role, created_at)
            VALUES (@id, @email, @name, @password_hash, @salt, @role, @created_at);
        """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@email", record.Email);
        cmd.Parameters.AddWithValue("@name", record.Name);
        cmd.Parameters.AddWithValue("@password_hash", record.PasswordHash);
        cmd.Parameters.AddWithValue("@salt", record.Salt);
        cmd.Parameters.AddWithValue("@role", record.Role);
        cmd.Parameters.AddWithValue("@created_at", record.CreatedAt);

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

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions (token, user_id, expires_at, created_at)
            VALUES (@token, @user_id, @expires_at, @created_at);
        """;
        cmd.Parameters.AddWithValue("@token", session.Token);
        cmd.Parameters.AddWithValue("@user_id", session.UserId);
        cmd.Parameters.AddWithValue("@expires_at", session.ExpiresAt);
        cmd.Parameters.AddWithValue("@created_at", session.CreatedAt);

        await cmd.ExecuteNonQueryAsync();
        return session;
    }

    public async Task<UserRecord?> ValidateSessionAsync(string token)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.id, u.email, u.name, u.password_hash, u.salt, u.role, u.created_at, s.expires_at, u.image
            FROM sessions s
            JOIN users u ON s.user_id = u.id
            WHERE s.token = @token;
        """;
        cmd.Parameters.AddWithValue("@token", token);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var expiresAt = reader.GetDateTime(7);
            if (expiresAt < DateTime.UtcNow) return null;

            return new UserRecord
            {
                Id = reader.GetString(0),
                Email = reader.GetString(1),
                Name = reader.GetString(2),
                PasswordHash = reader.GetString(3),
                Salt = reader.GetString(4),
                Role = reader.GetString(5),
                CreatedAt = reader.GetDateTime(6),
                Image = reader.IsDBNull(8) ? null : reader.GetString(8)
            };
        }
        return null;
    }

    public async Task InvalidateSessionAsync(string token)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sessions WHERE token = @token;";
        cmd.Parameters.AddWithValue("@token", token);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> UpdateUserProfileAsync(string userId, string name, string? image = null)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        if (image != null)
        {
            cmd.CommandText = "UPDATE users SET name = @name, image = @image WHERE id = @id;";
            cmd.Parameters.AddWithValue("@image", image);
        }
        else
        {
            cmd.CommandText = "UPDATE users SET name = @name WHERE id = @id;";
        }
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.Parameters.AddWithValue("@id", userId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<bool> UpdatePasswordAsync(string userId, string newPassword)
    {
        var salt = GenerateSalt();
        var hash = HashPassword(newPassword, salt);

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET password_hash = @hash, salt = @salt WHERE id = @id;";
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@salt", salt);
        cmd.Parameters.AddWithValue("@id", userId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<List<ConversationMeta>> ListConversationsAsync(string userId, CancellationToken ct = default)
    {
        var list = new List<ConversationMeta>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, created_at, updated_at, model_id, system_prompt, rules, version FROM conversations WHERE user_id = @user_id ORDER BY updated_at DESC;";
        cmd.Parameters.AddWithValue("@user_id", userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ConversationMeta
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                Created = reader.GetDateTime(2),
                Updated = reader.GetDateTime(3),
                ModelId = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SystemPromptOverride = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Rules = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Version = reader.IsDBNull(7) ? 1 : reader.GetInt32(7)
            });
        }
        return list;
    }

    public async Task<Conversation?> GetConversationAsync(string conversationId, string userId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data_json::text FROM conversations WHERE id = @id AND user_id = @user_id;";
        cmd.Parameters.AddWithValue("@id", conversationId);
        cmd.Parameters.AddWithValue("@user_id", userId);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<Conversation>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SaveConversationAsync(Conversation conversation, string userId, CancellationToken ct = default, int? expectedVersion = null)
    {
        try
        {
            var json = JsonSerializer.Serialize(conversation);
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO conversations (id, user_id, title, model_id, system_prompt, rules, created_at, updated_at, data_json, version)
                VALUES (@id, @user_id, @title, @model_id, @system_prompt, @rules, @created_at, @updated_at, @data_json::jsonb, @version)
                ON CONFLICT (id) DO UPDATE 
                SET title = @title, model_id = @model_id, system_prompt = @system_prompt, rules = @rules, updated_at = @updated_at, data_json = @data_json::jsonb, version = @version
                WHERE @expected_version IS NULL OR conversations.version = @expected_version;
            """;
            cmd.Parameters.AddWithValue("@id", conversation.Id);
            cmd.Parameters.AddWithValue("@user_id", userId);
            cmd.Parameters.AddWithValue("@title", conversation.Title);
            cmd.Parameters.AddWithValue("@model_id", (object?)conversation.ModelId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@system_prompt", (object?)conversation.SystemPromptOverride ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rules", (object?)conversation.Rules ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created_at", conversation.Created.UtcDateTime);
            cmd.Parameters.AddWithValue("@updated_at", conversation.Updated.UtcDateTime);
            cmd.Parameters.AddWithValue("@data_json", json);
            cmd.Parameters.AddWithValue("@version", conversation.Version);
            cmd.Parameters.AddWithValue("@expected_version", (object?)expectedVersion ?? DBNull.Value);

            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save conversation {Id} in PostgreSQL", conversation.Id);
            return false;
        }
    }

    public async Task<bool> DeleteConversationAsync(string conversationId, string userId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM conversations WHERE id = @id AND user_id = @user_id;";
        cmd.Parameters.AddWithValue("@id", conversationId);
        cmd.Parameters.AddWithValue("@user_id", userId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> SavePasskeyAsync(string userId, string credentialId, string publicKey, string name)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO user_passkeys (id, user_id, public_key, counter, name, created_at)
                VALUES (@id, @user_id, @public_key, 0, @name, NOW())
                ON CONFLICT (id) DO UPDATE SET
                    public_key = @public_key,
                    name = @name;
            """;
            cmd.Parameters.AddWithValue("@id", credentialId);
            cmd.Parameters.AddWithValue("@user_id", userId);
            cmd.Parameters.AddWithValue("@public_key", publicKey);
            cmd.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(name) ? "Passkey" : name);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save passkey {Id} for user {UserId}", credentialId, userId);
            return false;
        }
    }

    public async Task<List<PasskeyRecord>> GetPasskeysForUserAsync(string userId)
    {
        var list = new List<PasskeyRecord>();
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, user_id, public_key, counter, name, created_at FROM user_passkeys WHERE user_id = @user_id ORDER BY created_at DESC;";
            cmd.Parameters.AddWithValue("@user_id", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new PasskeyRecord
                {
                    Id = reader.GetString(0),
                    UserId = reader.GetString(1),
                    PublicKey = reader.GetString(2),
                    Counter = reader.GetInt64(3),
                    Name = reader.GetString(4),
                    CreatedAt = reader.GetDateTime(5)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list passkeys for user {UserId}", userId);
        }
        return list;
    }

    public async Task<PasskeyRecord?> GetPasskeyByIdAsync(string credentialId)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, user_id, public_key, counter, name, created_at FROM user_passkeys WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", credentialId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PasskeyRecord
                {
                    Id = reader.GetString(0),
                    UserId = reader.GetString(1),
                    PublicKey = reader.GetString(2),
                    Counter = reader.GetInt64(3),
                    Name = reader.GetString(4),
                    CreatedAt = reader.GetDateTime(5)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get passkey {Id}", credentialId);
        }
        return null;
    }

    public async Task<bool> DeletePasskeyAsync(string userId, string credentialId)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM user_passkeys WHERE id = @id AND user_id = @user_id;";
            cmd.Parameters.AddWithValue("@id", credentialId);
            cmd.Parameters.AddWithValue("@user_id", userId);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete passkey {Id} for user {UserId}", credentialId, userId);
            return false;
        }
    }

    public async Task<bool> HasPasskeysAsync(string userId)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM user_passkeys WHERE user_id = @user_id;";
            cmd.Parameters.AddWithValue("@user_id", userId);
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SavePasswordResetTokenAsync(string userId, string code, string token, DateTime expiresAt)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO password_reset_tokens (id, user_id, code, token, created_at, expires_at, used)
                VALUES (@id, @user_id, @code, @token, NOW(), @expires_at, FALSE);
            ";
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@user_id", userId);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@token", token);
            cmd.Parameters.AddWithValue("@expires_at", expiresAt);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save password reset token for user {UserId}", userId);
            return false;
        }
    }

    public async Task<PasswordResetRecord?> GetPasswordResetByCodeAsync(string email, string code)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT prt.id, prt.user_id, prt.code, prt.token, prt.created_at, prt.expires_at, prt.used
                FROM password_reset_tokens prt
                INNER JOIN users u ON prt.user_id = u.id
                WHERE LOWER(u.email) = LOWER(@email) AND prt.code = @code
                ORDER BY prt.created_at DESC
                LIMIT 1;
            ";
            cmd.Parameters.AddWithValue("@email", email);
            cmd.Parameters.AddWithValue("@code", code);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PasswordResetRecord
                {
                    Id = reader.GetString(0),
                    UserId = reader.GetString(1),
                    Code = reader.GetString(2),
                    Token = reader.GetString(3),
                    CreatedAt = reader.GetDateTime(4),
                    ExpiresAt = reader.GetDateTime(5),
                    Used = reader.GetBoolean(6)
                };
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query password reset by code for email {Email}", email);
            return null;
        }
    }

    public async Task<PasswordResetRecord?> GetPasswordResetByTokenAsync(string token)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, user_id, code, token, created_at, expires_at, used
                FROM password_reset_tokens
                WHERE token = @token
                ORDER BY created_at DESC
                LIMIT 1;
            ";
            cmd.Parameters.AddWithValue("@token", token);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PasswordResetRecord
                {
                    Id = reader.GetString(0),
                    UserId = reader.GetString(1),
                    Code = reader.GetString(2),
                    Token = reader.GetString(3),
                    CreatedAt = reader.GetDateTime(4),
                    ExpiresAt = reader.GetDateTime(5),
                    Used = reader.GetBoolean(6)
                };
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query password reset by token {Token}", token);
            return null;
        }
    }

    public async Task<bool> InvalidatePasswordResetTokenAsync(string tokenId)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE password_reset_tokens SET used = TRUE WHERE id = @id OR token = @id;";
            cmd.Parameters.AddWithValue("@id", tokenId);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate password reset token {TokenId}", tokenId);
            return false;
        }
    }

    private static string GenerateSalt()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashPassword(string password, string salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), 100_000, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(pbkdf2.GetBytes(32));
    }
}
