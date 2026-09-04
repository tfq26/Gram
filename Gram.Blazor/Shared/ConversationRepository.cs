namespace Gram.Blazor.Shared;

public interface IConversationRepository
{
    Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken cancellationToken = default);
    Task<Conversation?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<Conversation> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken = default);
    Task<Conversation?> SaveAsync(string id, SaveConversationRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// Backend-facing conversation boundary. ConversationService remains behind this
/// adapter during the storage migration and is initialized once per request scope.
public sealed class ConversationRepository : IConversationRepository
{
    private readonly ConversationService _service;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public ConversationRepository(ConversationService service) => _service = service;

    public async Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken cancellationToken = default)
    { await EnsureInitializedAsync(); return _service.Conversations.ToList(); }

    public async Task<Conversation?> GetAsync(string id, CancellationToken cancellationToken = default)
    { await EnsureInitializedAsync(); return _service.Conversations.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); }

    public async Task<Conversation> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var conversation = new Conversation { Title = string.IsNullOrWhiteSpace(request.Title) ? "New chat" : request.Title.Trim(), ModelId = request.ModelId };
        _service.Conversations.Add(conversation);
        await _service.SaveConversationAsync(conversation);
        return conversation;
    }

    public async Task<Conversation?> SaveAsync(string id, SaveConversationRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var conversation = _service.Conversations.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (conversation is null)
        {
            if (request.Version != 1) throw new ConversationVersionConflictException(1);
            conversation = new Conversation(id, string.IsNullOrWhiteSpace(request.Title) ? "New chat" : request.Title.Trim(), DateTimeOffset.UtcNow)
            { ModelId = request.ModelId, Messages = request.Messages?.ToList() ?? [] };
            _service.Conversations.Insert(0, conversation);
        }
        else
        {
            if (request.Version != conversation.Version) throw new ConversationVersionConflictException(conversation.Version);
            if (request.Title is not null) conversation.Title = request.Title.Trim();
            if (request.ModelId is not null) conversation.ModelId = request.ModelId;
            if (request.Messages is not null) conversation.Messages = request.Messages.ToList();
        }
        conversation.Updated = DateTimeOffset.UtcNow;
        await _service.SaveConversationAsync(conversation);
        return conversation;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    { await EnsureInitializedAsync(); if (!_service.Conversations.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) return false; await _service.DeleteConversationAsync(id); return true; }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        await _initializeLock.WaitAsync();
        try { if (!_initialized) { await _service.InitializeAsync(); _initialized = true; } }
        finally { _initializeLock.Release(); }
    }
}
