using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public string? Image { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class UserSessionRecord
{
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(30);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SqliteDatabaseService : IDatabaseProvider
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public string ProviderName => "SQLite (Local / Mini-Gram)";

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

    public Task InitializeAsync(CancellationToken ct = default)
    {
        InitializeDatabase();
        return Task.CompletedTask;
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
                image TEXT,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sessions (
                token TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS conversations (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                title TEXT NOT NULL,
                model_id TEXT,
                system_prompt TEXT,
                rules TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                data_json TEXT NOT NULL,
                version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS user_passkeys (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                public_key TEXT NOT NULL,
                counter INTEGER NOT NULL DEFAULT 0,
                name TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS password_reset_tokens (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                code TEXT NOT NULL,
                token TEXT NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                used INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                name TEXT NOT NULL,
                description TEXT,
                custom_instructions TEXT,
                color TEXT,
                icon TEXT,
                strict_context INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                data_json TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS sandbox_jobs (
                job_id TEXT PRIMARY KEY,
                request_json TEXT NOT NULL,
                status TEXT NOT NULL,
                sandbox_id TEXT,
                error TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sandbox_outbox (
                message_id TEXT PRIMARY KEY,
                job_id TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                published_at TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_sessions_user_id ON sessions(user_id);
            CREATE INDEX IF NOT EXISTS idx_conversations_user_id ON conversations(user_id);
            CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
            CREATE INDEX IF NOT EXISTS idx_user_passkeys_user_id ON user_passkeys(user_id);
            CREATE INDEX IF NOT EXISTS idx_reset_user_id ON password_reset_tokens(user_id);
            CREATE INDEX IF NOT EXISTS idx_reset_code ON password_reset_tokens(code);
            CREATE INDEX IF NOT EXISTS idx_reset_token ON password_reset_tokens(token);
            CREATE INDEX IF NOT EXISTS idx_projects_user_id ON projects(user_id);
            CREATE INDEX IF NOT EXISTS idx_sandbox_jobs_status ON sandbox_jobs(status, created_at);
            CREATE INDEX IF NOT EXISTS idx_sandbox_outbox_pending ON sandbox_outbox(published_at, created_at);
        ";
        cmd.ExecuteNonQuery();

        // Ensure image column exists on older schemas
        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE users ADD COLUMN image TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch {}

        try
        {
            using var alterConversation = conn.CreateCommand();
            alterConversation.CommandText = "ALTER TABLE conversations ADD COLUMN version INTEGER NOT NULL DEFAULT 1;";
            alterConversation.ExecuteNonQuery();
        }
        catch {}

        try
        {
            using var alterConversation = conn.CreateCommand();
            alterConversation.CommandText = "ALTER TABLE conversations ADD COLUMN project_id TEXT;";
            alterConversation.ExecuteNonQuery();
        }
        catch {}

        try
        {
            using var alterConversation = conn.CreateCommand();
            alterConversation.CommandText = "ALTER TABLE conversations ADD COLUMN is_archived INTEGER NOT NULL DEFAULT 0;";
            alterConversation.ExecuteNonQuery();
        }
        catch {}

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
        cmd.CommandText = "SELECT id, email, name, password_hash, salt, role, created_at, image FROM users WHERE LOWER(email) = LOWER(@email);";
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
                CreatedAt = DateTime.Parse(reader.GetString(6)),
                Image = reader.IsDBNull(7) ? null : reader.GetString(7)
            };
        }
        return null;
    }

    public async Task<UserRecord?> GetUserByIdAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, email, name, password_hash, salt, role, created_at, image FROM users WHERE id = @id;";
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
                CreatedAt = DateTime.Parse(reader.GetString(6)),
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
            SELECT u.id, u.email, u.name, u.password_hash, u.salt, u.role, u.created_at, s.expires_at, u.image
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
                CreatedAt = DateTime.Parse(reader.GetString(6)),
                Image = reader.IsDBNull(8) ? null : reader.GetString(8)
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

    public async Task<bool> UpdateUserProfileAsync(string userId, string name, string? image = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
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

    public async Task<List<ConversationMeta>> ListConversationsAsync(string userId, CancellationToken ct = default)
    {
        var list = new List<ConversationMeta>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, created_at, updated_at, model_id, system_prompt, rules, version, project_id, is_archived FROM conversations WHERE user_id = @user_id ORDER BY updated_at DESC;";
        cmd.Parameters.AddWithValue("@user_id", userId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ConversationMeta
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                Created = DateTimeOffset.Parse(reader.GetString(2)),
                Updated = DateTimeOffset.Parse(reader.GetString(3)),
                ModelId = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SystemPromptOverride = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Rules = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Version = reader.IsDBNull(7) ? 1 : reader.GetInt32(7),
                ProjectId = reader.IsDBNull(8) ? null : reader.GetString(8),
                IsArchived = !reader.IsDBNull(9) && reader.GetInt32(9) == 1
            });
        }
        return list;
    }

    public async Task<Conversation?> GetConversationAsync(string conversationId, string userId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data_json FROM conversations WHERE id = @id AND user_id = @user_id;";
        cmd.Parameters.AddWithValue("@id", conversationId);
        cmd.Parameters.AddWithValue("@user_id", userId);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Conversation>(json);
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
            var json = System.Text.Json.JsonSerializer.Serialize(conversation);
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO conversations (id, user_id, title, model_id, system_prompt, rules, created_at, updated_at, data_json, version, project_id, is_archived)
                VALUES (@id, @user_id, @title, @model_id, @system_prompt, @rules, @created_at, @updated_at, @data_json, @version, @project_id, @is_archived)
                ON CONFLICT (id) DO UPDATE 
                SET title = @title, model_id = @model_id, system_prompt = @system_prompt, rules = @rules, updated_at = @updated_at, data_json = @data_json, version = @version, project_id = @project_id, is_archived = @is_archived
                WHERE @expected_version IS NULL OR conversations.version = @expected_version;
            """;
            cmd.Parameters.AddWithValue("@id", conversation.Id);
            cmd.Parameters.AddWithValue("@user_id", userId);
            cmd.Parameters.AddWithValue("@title", conversation.Title);
            cmd.Parameters.AddWithValue("@model_id", (object?)conversation.ModelId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@system_prompt", (object?)conversation.SystemPromptOverride ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rules", (object?)conversation.Rules ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created_at", conversation.Created.ToString("o"));
            cmd.Parameters.AddWithValue("@updated_at", conversation.Updated.ToString("o"));
            cmd.Parameters.AddWithValue("@data_json", json);
            cmd.Parameters.AddWithValue("@version", conversation.Version);
            cmd.Parameters.AddWithValue("@project_id", (object?)conversation.ProjectId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@is_archived", conversation.IsArchived ? 1 : 0);
            cmd.Parameters.AddWithValue("@expected_version", (object?)expectedVersion ?? DBNull.Value);

            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteConversationAsync(string conversationId, string userId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM conversations WHERE id = @id AND user_id = @user_id;";
        cmd.Parameters.AddWithValue("@id", conversationId);
        cmd.Parameters.AddWithValue("@user_id", userId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<List<Project>> ListProjectsAsync(string userId, CancellationToken ct = default)
    {
        var list = new List<Project>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data_json FROM projects WHERE user_id = @user_id ORDER BY updated_at DESC;";
        cmd.Parameters.AddWithValue("@user_id", userId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var json = reader.GetString(0);
            try
            {
                var project = System.Text.Json.JsonSerializer.Deserialize<Project>(json);
                if (project != null) list.Add(project);
            }
            catch {}
        }
        return list;
    }

    public async Task<Project?> GetProjectAsync(string projectId, string userId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data_json FROM projects WHERE id = @id AND user_id = @user_id;";
        cmd.Parameters.AddWithValue("@id", projectId);
        cmd.Parameters.AddWithValue("@user_id", userId);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Project>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SaveProjectAsync(Project project, string userId, CancellationToken ct = default)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(project);
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO projects (id, user_id, name, description, custom_instructions, color, icon, strict_context, created_at, updated_at, data_json)
                VALUES (@id, @user_id, @name, @description, @custom_instructions, @color, @icon, @strict_context, @created_at, @updated_at, @data_json)
                ON CONFLICT (id) DO UPDATE 
                SET name = @name, description = @description, custom_instructions = @custom_instructions, color = @color, icon = @icon, strict_context = @strict_context, updated_at = @updated_at, data_json = @data_json;
            """;
            cmd.Parameters.AddWithValue("@id", project.Id);
            cmd.Parameters.AddWithValue("@user_id", userId);
            cmd.Parameters.AddWithValue("@name", project.Name);
            cmd.Parameters.AddWithValue("@description", (object?)project.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@custom_instructions", (object?)project.CustomInstructions ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@color", (object?)project.Color ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@icon", (object?)project.Icon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@strict_context", project.StrictContextOnly ? 1 : 0);
            cmd.Parameters.AddWithValue("@created_at", project.Created.ToString("o"));
            cmd.Parameters.AddWithValue("@updated_at", project.Updated.ToString("o"));
            cmd.Parameters.AddWithValue("@data_json", json);

            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteProjectAsync(string projectId, string userId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM projects WHERE id = @id AND user_id = @user_id;";
        cmd.Parameters.AddWithValue("@id", projectId);
        cmd.Parameters.AddWithValue("@user_id", userId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> SavePasskeyAsync(string userId, string credentialId, string publicKey, string name)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO user_passkeys (id, user_id, public_key, counter, name, created_at)
                VALUES (@id, @user_id, @public_key, 0, @name, @created_at)
                ON CONFLICT (id) DO UPDATE SET
                    public_key = @public_key,
                    name = @name;
            """;
            cmd.Parameters.AddWithValue("@id", credentialId);
            cmd.Parameters.AddWithValue("@user_id", userId);
            cmd.Parameters.AddWithValue("@public_key", publicKey);
            cmd.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(name) ? "Passkey" : name);
            cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow.ToString("o"));
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<PasskeyRecord>> GetPasskeysForUserAsync(string userId)
    {
        var list = new List<PasskeyRecord>();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, user_id, public_key, counter, name, created_at FROM user_passkeys WHERE user_id = @user_id ORDER BY created_at DESC;";
            cmd.Parameters.AddWithValue("@user_id", userId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new PasskeyRecord
                {
                    Id = reader.GetString(0),
                    UserId = reader.GetString(1),
                    PublicKey = reader.GetString(2),
                    Counter = reader.GetInt64(3),
                    Name = reader.GetString(4),
                    CreatedAt = DateTime.TryParse(reader.GetString(5), out var dt) ? dt : DateTime.UtcNow
                });
            }
        }
        catch {}
        return list;
    }

    public async Task<PasskeyRecord?> GetPasskeyByIdAsync(string credentialId)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, user_id, public_key, counter, name, created_at FROM user_passkeys WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", credentialId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PasskeyRecord
                {
                    Id = reader.GetString(0),
                    UserId = reader.GetString(1),
                    PublicKey = reader.GetString(2),
                    Counter = reader.GetInt64(3),
                    Name = reader.GetString(4),
                    CreatedAt = DateTime.TryParse(reader.GetString(5), out var dt) ? dt : DateTime.UtcNow
                };
            }
        }
        catch {}
        return null;
    }

    public async Task<bool> DeletePasskeyAsync(string userId, string credentialId)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM user_passkeys WHERE id = @id AND user_id = @user_id;";
            cmd.Parameters.AddWithValue("@id", credentialId);
            cmd.Parameters.AddWithValue("@user_id", userId);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> HasPasskeysAsync(string userId)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
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
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO password_reset_tokens (id, user_id, code, token, created_at, expires_at, used)
                VALUES (@id, @user_id, @code, @token, @created_at, @expires_at, 0);
            ";
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@user_id", userId);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@token", token);
            cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@expires_at", expiresAt.ToString("o"));
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<PasswordResetRecord?> GetPasswordResetByCodeAsync(string email, string code)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
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
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PasswordResetRecord
                {
                    Id = reader.GetString(0),
                    UserId = reader.GetString(1),
                    Code = reader.GetString(2),
                    Token = reader.GetString(3),
                    CreatedAt = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    ExpiresAt = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    Used = reader.GetInt32(6) == 1
                };
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<PasswordResetRecord?> GetPasswordResetByTokenAsync(string token)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, user_id, code, token, created_at, expires_at, used
                FROM password_reset_tokens
                WHERE token = @token
                ORDER BY created_at DESC
                LIMIT 1;
            ";
            cmd.Parameters.AddWithValue("@token", token);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PasswordResetRecord
                {
                    Id = reader.GetString(0),
                    UserId = reader.GetString(1),
                    Code = reader.GetString(2),
                    Token = reader.GetString(3),
                    CreatedAt = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    ExpiresAt = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    Used = reader.GetInt32(6) == 1
                };
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> InvalidatePasswordResetTokenAsync(string tokenId)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE password_reset_tokens SET used = 1 WHERE id = @id OR token = @id;";
            cmd.Parameters.AddWithValue("@id", tokenId);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task SaveSandboxJobAsync(SandboxJobRecord job, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sandbox_jobs (job_id, request_json, status, sandbox_id, error, created_at, updated_at)
            VALUES (@id, @request, @status, @sandbox, @error, @created, @updated)
            ON CONFLICT(job_id) DO UPDATE SET request_json=@request, status=@status, sandbox_id=@sandbox, error=@error, updated_at=@updated;";
        cmd.Parameters.AddWithValue("@id", job.JobId);
        cmd.Parameters.AddWithValue("@request", JsonSerializer.Serialize(job.Request));
        cmd.Parameters.AddWithValue("@status", job.Status.ToString());
        cmd.Parameters.AddWithValue("@sandbox", (object?)job.SandboxId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error", (object?)job.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", job.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@updated", job.UpdatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveSandboxJobWithOutboxAsync(SandboxJobRecord job, SandboxOutboxRecord message, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        using var transaction = conn.BeginTransaction();
        using var jobCmd = conn.CreateCommand();
        jobCmd.Transaction = transaction;
        jobCmd.CommandText = "INSERT INTO sandbox_jobs (job_id, request_json, status, sandbox_id, error, created_at, updated_at) VALUES (@id,@request,@status,@sandbox,@error,@created,@updated) ON CONFLICT(job_id) DO UPDATE SET request_json=@request,status=@status,sandbox_id=@sandbox,error=@error,updated_at=@updated;";
        jobCmd.Parameters.AddWithValue("@id", job.JobId); jobCmd.Parameters.AddWithValue("@request", JsonSerializer.Serialize(job.Request)); jobCmd.Parameters.AddWithValue("@status", job.Status.ToString()); jobCmd.Parameters.AddWithValue("@sandbox", (object?)job.SandboxId ?? DBNull.Value); jobCmd.Parameters.AddWithValue("@error", (object?)job.Error ?? DBNull.Value); jobCmd.Parameters.AddWithValue("@created", job.CreatedAt.ToString("o")); jobCmd.Parameters.AddWithValue("@updated", job.UpdatedAt.ToString("o"));
        await jobCmd.ExecuteNonQueryAsync(ct);
        using var outboxCmd = conn.CreateCommand();
        outboxCmd.Transaction = transaction;
        outboxCmd.CommandText = "INSERT OR IGNORE INTO sandbox_outbox (message_id, job_id, payload_json, created_at) VALUES (@id,@job,@payload,@created);";
        outboxCmd.Parameters.AddWithValue("@id", message.MessageId); outboxCmd.Parameters.AddWithValue("@job", message.JobId); outboxCmd.Parameters.AddWithValue("@payload", message.Payload); outboxCmd.Parameters.AddWithValue("@created", message.CreatedAt.ToString("o"));
        await outboxCmd.ExecuteNonQueryAsync(ct);
        transaction.Commit();
    }

    public async Task<SandboxJobRecord?> GetSandboxJobAsync(string jobId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT job_id, request_json, status, sandbox_id, error, created_at, updated_at FROM sandbox_jobs WHERE job_id=@id;";
        cmd.Parameters.AddWithValue("@id", jobId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await ReadSandboxJobAsync(reader, ct);
    }

    public async Task<List<SandboxJobRecord>> ListSandboxJobsAsync(CancellationToken ct = default)
    {
        var jobs = new List<SandboxJobRecord>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT job_id, request_json, status, sandbox_id, error, created_at, updated_at FROM sandbox_jobs ORDER BY created_at;";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var job = ParseSandboxJob(reader);
            if (job is not null) jobs.Add(job);
        }
        return jobs;
    }

    public async Task<List<SandboxOutboxRecord>> ListPendingSandboxOutboxAsync(int limit = 100, CancellationToken ct = default)
    {
        var messages = new List<SandboxOutboxRecord>();
        using var conn = new SqliteConnection(_connectionString); await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand(); cmd.CommandText = "SELECT message_id, job_id, payload_json, created_at, published_at FROM sandbox_outbox WHERE published_at IS NULL ORDER BY created_at LIMIT @limit;"; cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 1000));
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) messages.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3)), reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4))));
        return messages;
    }

    public async Task MarkSandboxOutboxPublishedAsync(string messageId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_connectionString); await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand(); cmd.CommandText = "UPDATE sandbox_outbox SET published_at=@published WHERE message_id=@id AND published_at IS NULL;"; cmd.Parameters.AddWithValue("@published", DateTimeOffset.UtcNow.ToString("o")); cmd.Parameters.AddWithValue("@id", messageId); await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<SandboxJobRecord?> ReadSandboxJobAsync(SqliteDataReader reader, CancellationToken ct)
        => await reader.ReadAsync(ct) ? ParseSandboxJob(reader) : null;

    private static SandboxJobRecord? ParseSandboxJob(SqliteDataReader reader)
    {
        try
        {
            var request = JsonSerializer.Deserialize<SandboxJobRequest>(reader.GetString(1));
            return request is null ? null : new SandboxJobRecord(reader.GetString(0), request, Enum.Parse<SandboxJobStatus>(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), DateTimeOffset.Parse(reader.GetString(5)), DateTimeOffset.Parse(reader.GetString(6)));
        }
        catch { return null; }
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
