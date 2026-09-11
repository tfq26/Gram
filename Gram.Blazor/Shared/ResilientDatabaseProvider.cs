using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

/// <summary>
/// Resilient database provider that automatically falls back to local SQLite when PostgreSQL
/// is unreachable, down, or encounters socket/connection errors.
/// </summary>
public sealed class ResilientDatabaseProvider : IDatabaseProvider
{
    private readonly ILogger<ResilientDatabaseProvider> _logger;
    private readonly PostgresDatabaseProvider _postgres;
    private readonly SqliteDatabaseService _sqlite;
    private bool _isPostgresDown = false;
    private DateTimeOffset _lastFailureTime = DateTimeOffset.MinValue;
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(2);

    public ResilientDatabaseProvider(
        ILogger<ResilientDatabaseProvider> logger,
        PostgresDatabaseProvider postgres,
        SqliteDatabaseService sqlite)
    {
        _logger = logger;
        _postgres = postgres;
        _sqlite = sqlite;
    }

    public string ProviderName => _isPostgresDown
        ? $"SQLite (Fallback from down PostgreSQL)"
        : $"PostgreSQL with Local SQLite Fallback";

    private bool ShouldAttemptPostgres()
    {
        if (!_isPostgresDown) return true;
        // Periodic check to see if Postgres has come back up
        if (DateTimeOffset.UtcNow - _lastFailureTime > RetryInterval)
        {
            return true;
        }
        return false;
    }

    private void MarkPostgresDown(Exception ex)
    {
        if (!_isPostgresDown)
        {
            _logger.LogWarning(ex, "[ResilientDatabaseProvider] PostgreSQL appears to be down or unreachable. Gracefully falling back to local SQLite database so operations continue uninterrupted.");
        }
        _isPostgresDown = true;
        _lastFailureTime = DateTimeOffset.UtcNow;
    }

    private void MarkPostgresUp()
    {
        if (_isPostgresDown)
        {
            _logger.LogInformation("[ResilientDatabaseProvider] PostgreSQL connection restored. Resuming primary PostgreSQL storage.");
            _isPostgresDown = false;
        }
    }

    private async Task<T> ExecuteWithFallbackAsync<T>(Func<PostgresDatabaseProvider, Task<T>> pgAction, Func<SqliteDatabaseService, Task<T>> sqliteAction)
    {
        if (ShouldAttemptPostgres())
        {
            try
            {
                var result = await pgAction(_postgres);
                MarkPostgresUp();
                return result;
            }
            catch (Exception ex) when (IsConnectionException(ex))
            {
                MarkPostgresDown(ex);
            }
        }

        return await sqliteAction(_sqlite);
    }

    private async Task ExecuteWithFallbackAsync(Func<PostgresDatabaseProvider, Task> pgAction, Func<SqliteDatabaseService, Task> sqliteAction)
    {
        if (ShouldAttemptPostgres())
        {
            try
            {
                await pgAction(_postgres);
                MarkPostgresUp();
                return;
            }
            catch (Exception ex) when (IsConnectionException(ex))
            {
                MarkPostgresDown(ex);
            }
        }

        await sqliteAction(_sqlite);
    }

    private static bool IsConnectionException(Exception ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return ex is Npgsql.NpgsqlException
            || ex is System.Net.Sockets.SocketException
            || ex is TimeoutException
            || msg.Contains("connection")
            || msg.Contains("refused")
            || msg.Contains("timeout")
            || (ex.InnerException != null && IsConnectionException(ex.InnerException));
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Always initialize SQLite so it's ready as immediate fallback
        await _sqlite.InitializeAsync(ct);

        try
        {
            await _postgres.InitializeAsync(ct);
            _isPostgresDown = false;
        }
        catch (Exception ex) when (IsConnectionException(ex))
        {
            MarkPostgresDown(ex);
        }
    }

    // User & Authentication
    public Task<UserRecord?> GetUserByEmailAsync(string email)
        => ExecuteWithFallbackAsync(p => p.GetUserByEmailAsync(email), s => s.GetUserByEmailAsync(email));

    public Task<UserRecord?> GetUserByIdAsync(string id)
        => ExecuteWithFallbackAsync(p => p.GetUserByIdAsync(id), s => s.GetUserByIdAsync(id));

    public Task<UserRecord> CreateUserAsync(string email, string name, string password, string role = "user")
        => ExecuteWithFallbackAsync(p => p.CreateUserAsync(email, name, password, role), s => s.CreateUserAsync(email, name, password, role));

    public Task<UserSessionRecord> CreateSessionAsync(string userId)
        => ExecuteWithFallbackAsync(p => p.CreateSessionAsync(userId), s => s.CreateSessionAsync(userId));

    public Task<UserRecord?> ValidateSessionAsync(string token)
        => ExecuteWithFallbackAsync(p => p.ValidateSessionAsync(token), s => s.ValidateSessionAsync(token));

    public Task InvalidateSessionAsync(string token)
        => ExecuteWithFallbackAsync(p => p.InvalidateSessionAsync(token), s => s.InvalidateSessionAsync(token));

    public Task<bool> UpdateUserProfileAsync(string userId, string name, string? image = null)
        => ExecuteWithFallbackAsync(p => p.UpdateUserProfileAsync(userId, name, image), s => s.UpdateUserProfileAsync(userId, name, image));

    public Task<bool> UpdatePasswordAsync(string userId, string newPassword)
        => ExecuteWithFallbackAsync(p => p.UpdatePasswordAsync(userId, newPassword), s => s.UpdatePasswordAsync(userId, newPassword));

    // Passkeys
    public Task<bool> SavePasskeyAsync(string userId, string credentialId, string publicKey, string name)
        => ExecuteWithFallbackAsync(p => p.SavePasskeyAsync(userId, credentialId, publicKey, name), s => s.SavePasskeyAsync(userId, credentialId, publicKey, name));

    public Task<List<PasskeyRecord>> GetPasskeysForUserAsync(string userId)
        => ExecuteWithFallbackAsync(p => p.GetPasskeysForUserAsync(userId), s => s.GetPasskeysForUserAsync(userId));

    public Task<PasskeyRecord?> GetPasskeyByIdAsync(string credentialId)
        => ExecuteWithFallbackAsync(p => p.GetPasskeyByIdAsync(credentialId), s => s.GetPasskeyByIdAsync(credentialId));

    public Task<bool> DeletePasskeyAsync(string userId, string credentialId)
        => ExecuteWithFallbackAsync(p => p.DeletePasskeyAsync(userId, credentialId), s => s.DeletePasskeyAsync(userId, credentialId));

    public Task<bool> HasPasskeysAsync(string userId)
        => ExecuteWithFallbackAsync(p => p.HasPasskeysAsync(userId), s => s.HasPasskeysAsync(userId));

    // Password Reset
    public Task<bool> SavePasswordResetTokenAsync(string userId, string code, string token, DateTime expiresAt)
        => ExecuteWithFallbackAsync(p => p.SavePasswordResetTokenAsync(userId, code, token, expiresAt), s => s.SavePasswordResetTokenAsync(userId, code, token, expiresAt));

    public Task<PasswordResetRecord?> GetPasswordResetByCodeAsync(string email, string code)
        => ExecuteWithFallbackAsync(p => p.GetPasswordResetByCodeAsync(email, code), s => s.GetPasswordResetByCodeAsync(email, code));

    public Task<PasswordResetRecord?> GetPasswordResetByTokenAsync(string token)
        => ExecuteWithFallbackAsync(p => p.GetPasswordResetByTokenAsync(token), s => s.GetPasswordResetByTokenAsync(token));

    public Task<bool> InvalidatePasswordResetTokenAsync(string tokenId)
        => ExecuteWithFallbackAsync(p => p.InvalidatePasswordResetTokenAsync(tokenId), s => s.InvalidatePasswordResetTokenAsync(tokenId));

    // Conversation & Message Persistence
    public Task<List<ConversationMeta>> ListConversationsAsync(string userId, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.ListConversationsAsync(userId, ct), s => s.ListConversationsAsync(userId, ct));

    public Task<Conversation?> GetConversationAsync(string conversationId, string userId, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.GetConversationAsync(conversationId, userId, ct), s => s.GetConversationAsync(conversationId, userId, ct));

    public Task<bool> SaveConversationAsync(Conversation conversation, string userId, CancellationToken ct = default, int? expectedVersion = null)
        => ExecuteWithFallbackAsync(p => p.SaveConversationAsync(conversation, userId, ct, expectedVersion), s => s.SaveConversationAsync(conversation, userId, ct, expectedVersion));

    public Task<bool> DeleteConversationAsync(string conversationId, string userId, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.DeleteConversationAsync(conversationId, userId, ct), s => s.DeleteConversationAsync(conversationId, userId, ct));

    // Project Operations
    public Task<List<Project>> ListProjectsAsync(string userId, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.ListProjectsAsync(userId, ct), s => s.ListProjectsAsync(userId, ct));

    public Task<Project?> GetProjectAsync(string projectId, string userId, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.GetProjectAsync(projectId, userId, ct), s => s.GetProjectAsync(projectId, userId, ct));

    public Task<bool> SaveProjectAsync(Project project, string userId, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.SaveProjectAsync(project, userId, ct), s => s.SaveProjectAsync(project, userId, ct));

    public Task<bool> DeleteProjectAsync(string projectId, string userId, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.DeleteProjectAsync(projectId, userId, ct), s => s.DeleteProjectAsync(projectId, userId, ct));

    // Sandbox job persistence
    public Task SaveSandboxJobAsync(SandboxJobRecord job, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.SaveSandboxJobAsync(job, ct), s => s.SaveSandboxJobAsync(job, ct));

    public Task SaveSandboxJobWithOutboxAsync(SandboxJobRecord job, SandboxOutboxRecord message, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.SaveSandboxJobWithOutboxAsync(job, message, ct), s => s.SaveSandboxJobWithOutboxAsync(job, message, ct));

    public Task<SandboxJobRecord?> GetSandboxJobAsync(string jobId, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.GetSandboxJobAsync(jobId, ct), s => s.GetSandboxJobAsync(jobId, ct));

    public Task<List<SandboxJobRecord>> ListSandboxJobsAsync(CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.ListSandboxJobsAsync(ct), s => s.ListSandboxJobsAsync(ct));

    public Task<List<SandboxOutboxRecord>> ListPendingSandboxOutboxAsync(int limit = 100, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.ListPendingSandboxOutboxAsync(limit, ct), s => s.ListPendingSandboxOutboxAsync(limit, ct));

    public Task MarkSandboxOutboxPublishedAsync(string messageId, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.MarkSandboxOutboxPublishedAsync(messageId, ct), s => s.MarkSandboxOutboxPublishedAsync(messageId, ct));

    // Terminal log persistence
    public Task SaveTerminalLogAsync(TerminalLogRecord record, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.SaveTerminalLogAsync(record, ct), s => s.SaveTerminalLogAsync(record, ct));

    public Task<List<TerminalLogRecord>> ListTerminalLogsAsync(string? workspacePath = null, int limit = 100, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.ListTerminalLogsAsync(workspacePath, limit, ct), s => s.ListTerminalLogsAsync(workspacePath, limit, ct));

    public Task ClearTerminalLogsAsync(string? workspacePath = null, CancellationToken ct = default)
        => ExecuteWithFallbackAsync(p => p.ClearTerminalLogsAsync(workspacePath, ct), s => s.ClearTerminalLogsAsync(workspacePath, ct));
}
