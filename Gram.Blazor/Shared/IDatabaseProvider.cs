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

    // Conversation & Message Persistence
    Task<List<ConversationMeta>> ListConversationsAsync(string userId, CancellationToken ct = default);
    Task<Conversation?> GetConversationAsync(string conversationId, string userId, CancellationToken ct = default);
    Task<bool> SaveConversationAsync(Conversation conversation, string userId, CancellationToken ct = default);
    Task<bool> DeleteConversationAsync(string conversationId, string userId, CancellationToken ct = default);
}
