using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Gram.Blazor.Shared;

public sealed class BackendFacade : IConversationApi, IRunApi, IModelApi, ISearchApi, IArtifactApi, ILocalAgentApi
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentRunTracker _tracker;
    private readonly ModelService _models;

    public BackendFacade(IServiceScopeFactory scopeFactory, IAgentRunTracker tracker, ModelService models)
    { _scopeFactory = scopeFactory; _tracker = tracker; _models = models; }

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ConversationService>();
        await service.InitializeAsync();
        return service.Conversations.Select(ToSummary).ToList();
    }

    public async Task<ConversationDetail?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ConversationService>();
        await service.InitializeAsync();
        var conversation = service.Conversations.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (conversation is null && id.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            conversation = new Conversation("default", "New chat", DateTimeOffset.UtcNow);
            service.Conversations.Insert(0, conversation);
            await service.SaveConversationAsync(conversation);
        }
        return conversation is null ? null : ToDetail(conversation);
    }

    public async Task<ConversationDetail> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ConversationService>();
        await service.InitializeAsync();
        var conversation = new Conversation { Title = string.IsNullOrWhiteSpace(request.Title) ? "New chat" : request.Title.Trim(), ModelId = request.ModelId };
        service.Conversations.Add(conversation);
        await service.SaveConversationAsync(conversation);
        return ToDetail(conversation);
    }

    public async Task<ConversationDetail?> SaveAsync(string id, SaveConversationRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ConversationService>();
        await service.InitializeAsync();
        var conversation = service.Conversations.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (conversation is null)
        {
            conversation = new Conversation(id, string.IsNullOrWhiteSpace(request.Title) ? "New chat" : request.Title.Trim(), DateTimeOffset.UtcNow)
            {
                ModelId = request.ModelId,
                Messages = request.Messages?.ToList() ?? new List<Message>()
            };
            service.Conversations.Insert(0, conversation);
        }
        else
        {
            if (request.Title is not null) conversation.Title = request.Title.Trim();
            if (request.ModelId is not null) conversation.ModelId = request.ModelId;
            if (request.Messages is not null) conversation.Messages = request.Messages.ToList();
        }
        conversation.Updated = DateTimeOffset.UtcNow;
        await service.SaveConversationAsync(conversation);
        return ToDetail(conversation);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ConversationService>();
        await service.InitializeAsync();
        if (!service.Conversations.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) return false;
        await service.DeleteConversationAsync(id);
        return true;
    }

    public async Task<RunSummary> StartAsync(string conversationId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) throw new ArgumentException("Text is required.", nameof(request));
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ConversationService>();
        await service.InitializeAsync();
        var conversation = service.Conversations.FirstOrDefault(x => x.Id.Equals(conversationId, StringComparison.OrdinalIgnoreCase));
        if (conversation is null)
        {
            conversation = new Conversation(conversationId, "New chat", DateTimeOffset.UtcNow)
            {
                ModelId = request.ModelId
            };
            service.Conversations.Insert(0, conversation);
        }

        var modelId = request.ModelId ?? conversation.ModelId ?? _models.Models.FirstOrDefault(x => x.IsFavorite)?.Id ?? _models.Models.FirstOrDefault()?.Id ?? "gram-q-plus";
        conversation.ModelId = modelId;
        if (conversation.Messages.LastOrDefault(x => x.Role.Equals("user", StringComparison.OrdinalIgnoreCase))?.Text != request.Text)
            conversation.Messages.Add(new Message("user", request.Text));
        conversation.Updated = DateTimeOffset.UtcNow;
        await service.SaveConversationAsync(conversation);
        var run = _tracker.Start(conversation.Id, modelId);
        _ = ExecuteRunAsync(run.Id, conversation, request, cancellationToken);
        return ToSummary(run);
    }

    private async Task ExecuteRunAsync(string runId, Conversation conversation, SendMessageRequest request, CancellationToken callerToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var router = scope.ServiceProvider.GetRequiredService<IAgentWorkflowRouter>();
            var presentationService = scope.ServiceProvider.GetRequiredService<PresentationGenerationService>();
            var run = _tracker.Get(runId)!;
            // The POST request may finish before this durable run does. Cancellation is
            // therefore owned by the run tracker, not by the HTTP request lifetime.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_tracker.GetCancellationToken(runId));
            var workspace = string.IsNullOrWhiteSpace(request.WorkspacePath) ? Environment.CurrentDirectory : request.WorkspacePath!;
            var history = request.ContextMessages?.ToList() ?? conversation.Messages;
            void ForwardPresentationUpdate(ContentProject project) => _tracker.EmitPayload(runId, "run.presentation", JsonSerializer.Serialize(project, BackendJson.Options));
            presentationService.ProjectUpdated += ForwardPresentationUpdate;
            try
            {
                await foreach (var step in router.RunAsync(new AgentWorkflowRequest(run.ModelId, request.Text, history, workspace, request.SystemPrompt, RepositoryMap: request.RepositoryMap, AllowedToolNames: request.AllowedToolNames, IsPresentationMode: request.PresentationMode, ForcedWebSearchQuery: request.ForcedSearchQuery, CancellationToken: linked.Token, AllowLocalCapabilities: request.AllowLocalCapabilities)))
                {
                    if (step.ContentDelta is { Length: > 0 } delta) _tracker.AppendOutput(runId, delta);
                    _tracker.Update(runId, AgentRunStatus.Executing, step.ToolCall is null ? "Generating response" : $"Using {ToolNameHelper.GetDisplayName(step.ToolCall.ToolName)}", toolName: step.ToolCall?.ToolName);
                    _tracker.EmitPayload(runId, "run.step", JsonSerializer.Serialize(new { step.ContentDelta, step.ThinkingDelta, step.ToolCall, step.ErrorMessage, step.IsComplete }, BackendJson.Options));
                    if (step.ErrorMessage is not null) _tracker.Complete(runId, AgentRunStatus.Failed, step.ErrorMessage);
                }
            }
            finally
            {
                presentationService.ProjectUpdated -= ForwardPresentationUpdate;
            }
            _tracker.Complete(runId);
            using var saveScope = _scopeFactory.CreateScope();
            var service = saveScope.ServiceProvider.GetRequiredService<ConversationService>();
            await service.InitializeAsync();
            var saved = service.Conversations.FirstOrDefault(x => x.Id == conversation.Id);
            if (saved is not null) { saved.Messages.Add(new Message("assistant", _tracker.GetOutput(runId) ?? "")); await service.SaveConversationAsync(saved); }
        }
        catch (OperationCanceledException) { _tracker.Complete(runId, AgentRunStatus.Cancelled, "Generation cancelled."); }
        catch (Exception ex) { _tracker.Complete(runId, AgentRunStatus.Failed, ex.Message); }
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
