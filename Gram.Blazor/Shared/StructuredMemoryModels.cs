using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public static class StructuredMemoryKinds
{
    public const string Fact = "fact";
    public const string Preference = "preference";
    public const string Relationship = "relationship";
    public const string Goal = "goal";
    public const string Decision = "decision";
    public const string Episode = "episode";
    public const string Constraint = "constraint";
}

public static class StructuredMemoryStatuses
{
    public const string Candidate = "candidate";
    public const string Active = "active";
    public const string Superseded = "superseded";
    public const string Forgotten = "forgotten";
}

public sealed class StructuredMemory
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("userId")] public string UserId { get; set; } = "default";
    [JsonPropertyName("projectId")] public string? ProjectId { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = StructuredMemoryKinds.Fact;
    [JsonPropertyName("subject")] public string Subject { get; set; } = "";
    [JsonPropertyName("predicate")] public string Predicate { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = StructuredMemoryStatuses.Active;
    [JsonPropertyName("confidence")] public double Confidence { get; set; } = 0.8;
    [JsonPropertyName("importance")] public double Importance { get; set; } = 0.5;
    [JsonPropertyName("sourceConversationId")] public string? SourceConversationId { get; set; }
    [JsonPropertyName("sourceMessageId")] public string? SourceMessageId { get; set; }
    [JsonPropertyName("evidence")] public string Evidence { get; set; } = "";
    [JsonPropertyName("validFrom")] public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("validUntil")] public DateTimeOffset? ValidUntil { get; set; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("supersedesId")] public string? SupersedesId { get; set; }
    [JsonPropertyName("lastConfirmedAt")] public DateTimeOffset? LastConfirmedAt { get; set; }
}

public sealed record RememberMemoryRequest(
    string Subject,
    string Predicate,
    string Value,
    string Kind = StructuredMemoryKinds.Fact,
    string? UserId = null,
    string? ProjectId = null,
    string? SourceConversationId = null,
    string? SourceMessageId = null,
    string? Evidence = null,
    double Confidence = 0.8,
    double Importance = 0.5,
    bool RequireConfirmation = false);

public sealed record MemorySearchRequest(string Query, string? UserId = null, string? ProjectId = null, int Limit = 12, bool IncludeCandidates = false);
