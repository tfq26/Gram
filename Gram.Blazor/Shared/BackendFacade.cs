using Microsoft.Extensions.DependencyInjection;

namespace Gram.Blazor.Shared;

public sealed class BackendFacade : IConversationApi, IRunApi, IModelApi, ISearchApi, IArtifactApi, ILocalAgentApi
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentRunTracker _tracker;
    private readonly ModelService _models;
    private readonly RunManager _runManager;
    private readonly IConversationRepository _conversations;

    public BackendFacade(IServiceScopeFactory scopeFactory, IAgentRunTracker tracker, ModelService models, RunManager runManager, IConversationRepository conversations)
    { _scopeFactory = scopeFactory; _tracker = tracker; _models = models; _runManager = runManager; _conversations = conversations; }

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var conversations = await _conversations.ListAsync(cancellationToken);
        return conversations.Select(ToSummary).ToList();
    }

    public async Task<ConversationDetail?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetAsync(id, cancellationToken);
        if (conversation is null && id.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            conversation = new Conversation("default", "New chat", DateTimeOffset.UtcNow);
            conversation = await _conversations.CreateAsync(new CreateConversationRequest("New chat"), cancellationToken);
        }
        return conversation is null ? null : ToDetail(conversation);
    }

    public async Task<ConversationDetail> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.CreateAsync(request, cancellationToken);
        return ToDetail(conversation);
    }

    public async Task<ConversationDetail?> SaveAsync(string id, SaveConversationRequest request, CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.SaveAsync(id, request, cancellationToken);
        return conversation is null ? null : ToDetail(conversation);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _conversations.DeleteAsync(id, cancellationToken);
    }

    public async Task<RunSummary> StartAsync(string conversationId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) throw new ArgumentException("Text is required.", nameof(request));
        var conversation = await _conversations.GetAsync(conversationId, cancellationToken);
        if (conversation is null)
        {
            conversation = new Conversation(conversationId, "New chat", DateTimeOffset.UtcNow)
            {
                ModelId = request.ModelId
            };
            conversation = await _conversations.CreateAsync(new CreateConversationRequest("New chat", request.ModelId), cancellationToken);
        }

        var modelId = request.ModelId ?? conversation.ModelId ?? _models.Models.FirstOrDefault(x => x.Id.Equals(GramModelIds.GramQ, StringComparison.OrdinalIgnoreCase))?.Id ?? _models.Models.FirstOrDefault(x => x.IsFavorite)?.Id ?? _models.Models.FirstOrDefault()?.Id ?? GramModelIds.GramQ;
        conversation.ModelId = modelId;
        if (conversation.Messages.LastOrDefault(x => x.Role.Equals("user", StringComparison.OrdinalIgnoreCase))?.Text != request.Text)
            conversation.Messages.Add(new Message("user", request.Text));
        conversation.Updated = DateTimeOffset.UtcNow;
        await _conversations.SaveAsync(conversation.Id, new SaveConversationRequest(conversation.Title, conversation.ModelId, conversation.Messages, conversation.Version), cancellationToken);
        var run = _runManager.Start(conversation, modelId, request);
        return ToSummary(run);
    }

    public RunSummary? Get(string runId) => _tracker.Get(runId) is { } run ? ToSummary(run) : null;
    public IReadOnlyList<RunEvent> GetEvents(string runId, long afterSequence = 0) => _tracker.GetEventsAfter(runId, afterSequence).Select(e => new RunEvent(e.RunId, e.Sequence, e.Timestamp, e.Type, e.Message)).ToList();
    public Task<bool> CancelAsync(string runId, string? reason = null) => Task.FromResult(_tracker.Cancel(runId, reason ?? "Cancelled by client."));
    public IReadOnlyList<ModelSummary> ListModels() => _models.Models.Select(x => new ModelSummary(x.Id, x.Name, x.Provider, x.Description, x.ContextLength, x.IsFavorite, x.IsEnabled)).ToList();
    public IReadOnlyList<ProviderStatus> ListProviders() => _models.Providers.Select(x => new ProviderStatus(x.Id, x.Name, x.Type.ToString(), x.IsConnected, x.IsSystemManaged, x.LastTested)).ToList();
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var providers = scope.ServiceProvider.GetServices<IWebSearchProvider>().Where(x => x.IsConfigured).ToList();
        if (providers.Count == 0) return [];
        var results = await providers[0].SearchAsync(request.Query, Math.Clamp(request.MaxResults, 1, 50), cancellationToken);
        return results.Select(x => new SearchResult(x.Title, x.Url, x.Snippet)).ToList();
    }
    public async Task<ArtifactSummary?> GetArtifactAsync(string id, CancellationToken cancellationToken = default)
    {
        var conversations = await ListAsync(cancellationToken);
        foreach (var summary in conversations)
        {
            var detail = await GetAsync(summary.Id, cancellationToken);
            var artifact = detail?.Artifacts.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (artifact is not null) return artifact;
        }
        return null;
    }
    Task<ArtifactSummary?> IArtifactApi.GetAsync(string id, CancellationToken cancellationToken) => GetArtifactAsync(id, cancellationToken);
    public async Task<ArtifactDetail?> GetContentAsync(string id, CancellationToken cancellationToken = default)
    {
        var conversations = await ListAsync(cancellationToken);
        foreach (var summary in conversations)
        {
            var detail = await GetAsync(summary.Id, cancellationToken);
            var artifact = detail?.Artifacts.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (artifact is null) continue;
            var source = await LoadConversationArtifactAsync(summary.Id, id, cancellationToken);
            return source is null ? null : new ArtifactDetail(artifact.Id, artifact.Name, artifact.ContentType, source.Content, source.CreatedAt);
        }
        return null;
    }

    private async Task<ArtifactInfo?> LoadConversationArtifactAsync(string conversationId, string artifactId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ConversationService>();
        var storage = scope.ServiceProvider.GetRequiredService<SessionStorageService>();
        await service.InitializeAsync();
        var artifact = service.Conversations.FirstOrDefault(x => x.Id == conversationId)?.Artifacts.FirstOrDefault(x => x.Id == artifactId);
        if (artifact is not null && string.IsNullOrEmpty(artifact.Content))
            artifact.Content = await storage.ReadArtifactTextAsync(conversationId, artifact.FileName) ?? "";
        return artifact;
    }
    public LocalAgentStatus GetStatus() => new(false, null, [new SandboxCapability("local-agent", false, "No local agent is paired.")]);

    private static ConversationSummary ToSummary(Conversation x) => new(x.Id, x.Title, x.Created, x.Updated, x.ModelId, x.Messages.Count, x.Artifacts.Count, x.Version);
    private static ConversationDetail ToDetail(Conversation x) => new(x.Id, x.Title, x.Created, x.Updated, x.ModelId, x.Messages, x.Artifacts.Select(a => new ArtifactSummary(a.Id, string.IsNullOrWhiteSpace(a.Title) ? a.FileName : a.Title, "text/html")).ToList(), x.Version);
    private static RunSummary ToSummary(AgentRunSnapshot x) => new(x.Id, x.ConversationId, x.ModelId, x.Status.ToString(), x.StatusText, x.StartedAt, x.UpdatedAt, x.CompletedAt, x.Error);
}
