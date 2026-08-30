using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

// Frontend-neutral contracts. Keep provider and implementation types out of this file.
public sealed record ConversationSummary(string Id, string Title, DateTimeOffset Created, DateTimeOffset Updated, string? ModelId, int MessageCount, int ArtifactCount, int Version = 1);
public sealed record ConversationDetail(string Id, string Title, DateTimeOffset Created, DateTimeOffset Updated, string? ModelId, IReadOnlyList<Message> Messages, IReadOnlyList<ArtifactSummary> Artifacts, int Version = 1);
public sealed record CreateConversationRequest(string? Title = null, string? ModelId = null);
public sealed record SaveConversationRequest(string? Title = null, string? ModelId = null, IReadOnlyList<Message>? Messages = null, int Version = 1);
public sealed record SendMessageRequest(string Text, string? ModelId = null, string? WorkspacePath = null, bool PresentationMode = false, string? ForcedSearchQuery = null, IReadOnlyList<Message>? ContextMessages = null, string? SystemPrompt = null, string? RepositoryMap = null, IReadOnlyList<string>? AllowedToolNames = null, bool AllowLocalCapabilities = false);
public sealed record CancelRunRequest(string? Reason = null);
public sealed record RunSummary(string Id, string ConversationId, string ModelId, string Status, string StatusText, DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, DateTimeOffset? CompletedAt, string? Error, int Version = 1);
public sealed record RunEvent(string RunId, long Sequence, DateTimeOffset Timestamp, string EventType, string Payload);
public sealed record ArtifactSummary(string Id, string Name, string ContentType, string? Url = null);
public sealed record ArtifactDetail(string Id, string Name, string ContentType, string Content, DateTimeOffset UpdatedAt, int Version = 1);
public sealed record ModelSummary(string Id, string Name, string Provider, string? Description, int? ContextLength, bool IsFavorite, bool IsEnabled);
public sealed record ProviderStatus(string Id, string Name, string Type, bool IsConnected, bool IsSystemManaged, DateTimeOffset? LastTested);
public sealed record SearchRequest(string Query, int MaxResults = 10);
public sealed record SearchResult(string Title, string Url, string? Snippet = null, DateTimeOffset? PublishedAt = null);
public sealed record SandboxCapability(string Name, bool Enabled, string? Description = null);
public sealed record LocalAgentStatus(bool IsAvailable, string? Version = null, IReadOnlyList<SandboxCapability>? Capabilities = null);
public sealed record ApiError(string Code, string Message, string? TraceId = null, IReadOnlyDictionary<string, string[]>? Details = null);
public sealed class ConversationVersionConflictException : Exception
{
    public int CurrentVersion { get; }
    public ConversationVersionConflictException(int currentVersion) : base("The conversation was changed by another client.") => CurrentVersion = currentVersion;
}

public interface IConversationApi
{
    Task<IReadOnlyList<ConversationSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<ConversationDetail?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<ConversationDetail> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken = default);
    Task<ConversationDetail?> SaveAsync(string id, SaveConversationRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
public interface IRunApi
{
    Task<RunSummary> StartAsync(string conversationId, SendMessageRequest request, CancellationToken cancellationToken = default);
    RunSummary? Get(string runId);
    IReadOnlyList<RunEvent> GetEvents(string runId, long afterSequence = 0);
    Task<bool> CancelAsync(string runId, string? reason = null);
}
public interface IModelApi { IReadOnlyList<ModelSummary> ListModels(); IReadOnlyList<ProviderStatus> ListProviders(); }
public interface ISearchApi { Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default); }
public interface IArtifactApi
{
    Task<ArtifactSummary?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<ArtifactDetail?> GetContentAsync(string id, CancellationToken cancellationToken = default);
}
public interface ILocalAgentApi { LocalAgentStatus GetStatus(); }

public static class BackendJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
