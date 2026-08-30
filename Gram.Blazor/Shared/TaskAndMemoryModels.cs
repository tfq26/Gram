using System;
using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public class GramTaskItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "default";

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "todo"; // todo, in_progress, done

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium"; // low, medium, high, urgent

    [JsonPropertyName("dueDate")]
    public DateTimeOffset? DueDate { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }
}

public class GramMemoryRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "default";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "preference"; // preference, instruction, project_context, personal

    [JsonPropertyName("ruleContent")]
    public string RuleContent { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class GramReminder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "default";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("remindAt")]
    public DateTimeOffset RemindAt { get; set; }

    [JsonPropertyName("isDismissed")]
    public bool IsDismissed { get; set; } = false;

    [JsonPropertyName("triggeredAt")]
    public DateTimeOffset? TriggeredAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
