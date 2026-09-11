namespace Gram.Blazor.Shared;

public interface IDatabaseProvider
{
    string ProviderName { get; }
    Task InitializeAsync(CancellationToken ct = default);

    // User & Authentication Operations
    Task<UserRecord?> GetUserByEmailAsync(string email);
    Task<UserRecord?> GetUserByIdAsync(string id);
    Task<UserRecord> CreateUserAsync(string email, string name, string password, string role = "user");
    Task<UserSessionRecord> CreateSessionAsync(string userId);
    Task<UserRecord?> ValidateSessionAsync(string token);
    Task InvalidateSessionAsync(string token);
    Task<bool> UpdateUserProfileAsync(string userId, string name, string? image = null);
    Task<bool> UpdatePasswordAsync(string userId, string newPassword);

    // Passkey Operations
    Task<bool> SavePasskeyAsync(string userId, string credentialId, string publicKey, string name);
    Task<List<PasskeyRecord>> GetPasskeysForUserAsync(string userId);
    Task<PasskeyRecord?> GetPasskeyByIdAsync(string credentialId);
    Task<bool> DeletePasskeyAsync(string userId, string credentialId);
    Task<bool> HasPasskeysAsync(string userId);

    // Password Reset Operations
    Task<bool> SavePasswordResetTokenAsync(string userId, string code, string token, DateTime expiresAt);
    Task<PasswordResetRecord?> GetPasswordResetByCodeAsync(string email, string code);
    Task<PasswordResetRecord?> GetPasswordResetByTokenAsync(string token);
    Task<bool> InvalidatePasswordResetTokenAsync(string tokenId);

    // Conversation & Message Persistence
    Task<List<ConversationMeta>> ListConversationsAsync(string userId, CancellationToken ct = default);
    Task<Conversation?> GetConversationAsync(string conversationId, string userId, CancellationToken ct = default);
    Task<bool> SaveConversationAsync(Conversation conversation, string userId, CancellationToken ct = default, int? expectedVersion = null);
    Task<bool> DeleteConversationAsync(string conversationId, string userId, CancellationToken ct = default);

    // Project Operations
    Task<List<Project>> ListProjectsAsync(string userId, CancellationToken ct = default);
    Task<Project?> GetProjectAsync(string projectId, string userId, CancellationToken ct = default);
    Task<bool> SaveProjectAsync(Project project, string userId, CancellationToken ct = default);
    Task<bool> DeleteProjectAsync(string projectId, string userId, CancellationToken ct = default);

    // Sandbox job persistence
    Task SaveSandboxJobAsync(SandboxJobRecord job, CancellationToken ct = default);
    Task SaveSandboxJobWithOutboxAsync(SandboxJobRecord job, SandboxOutboxRecord message, CancellationToken ct = default);
    Task<SandboxJobRecord?> GetSandboxJobAsync(string jobId, CancellationToken ct = default);
    Task<List<SandboxJobRecord>> ListSandboxJobsAsync(CancellationToken ct = default);
    Task<List<SandboxOutboxRecord>> ListPendingSandboxOutboxAsync(int limit = 100, CancellationToken ct = default);
    Task MarkSandboxOutboxPublishedAsync(string messageId, CancellationToken ct = default);

    // Terminal log persistence
    Task SaveTerminalLogAsync(TerminalLogRecord record, CancellationToken ct = default);
    Task<List<TerminalLogRecord>> ListTerminalLogsAsync(string? workspacePath = null, int limit = 100, CancellationToken ct = default);
    Task ClearTerminalLogsAsync(string? workspacePath = null, CancellationToken ct = default);
}

public sealed record TerminalLogRecord(
    string Id,
    string WorkspacePath,
    string Command,
    int ExitCode,
    string Stdout,
    string Stderr,
    long ElapsedMs,
    DateTimeOffset Timestamp
);
