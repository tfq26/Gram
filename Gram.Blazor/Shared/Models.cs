using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public sealed class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "New chat";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    public string? ModelId { get; set; }
    public string SystemPromptOverride { get; set; } = "";
    public string Rules { get; set; } = "";
    public List<Message> Messages { get; set; } = [];
    public List<AttachmentInfo> Attachments { get; set; } = [];
    public List<ArtifactInfo> Artifacts { get; set; } = [];
    public string? ContextSummary { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens => PromptTokens + CompletionTokens;
    public long LastLatencyMs { get; set; }
    public double EstimatedCostUsd { get; set; }

    public Conversation() {}

    public Conversation(string id, string title, DateTimeOffset updated)
    {
        Id = id;
        Title = title;
        Created = updated;
        Updated = updated;
    }
}

public sealed class ConversationMeta
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "New chat";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    public string? ModelId { get; set; }
    public string SystemPromptOverride { get; set; } = "";
    public string Rules { get; set; } = "";
    public int MessageCount { get; set; }
    public int AttachmentCount { get; set; }
    public int ArtifactCount { get; set; }
}

public sealed class Message
{
    public string Role { get; set; } = "user"; // "user", "assistant", "system", "tool"
    public string Text { get; set; } = "";
    public string? ErrorDetail { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? Thinking { get; set; }
    public List<ToolCallInfo>? ToolCalls { get; set; }

    public Message() {}
    public Message(string role, string text)
    {
        Role = role;
        Text = text;
        Timestamp = DateTimeOffset.UtcNow;
    }
}

public sealed class ToolCallInfo
{
    public string Id { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string? Result { get; set; }
    public bool IsError { get; set; }
}

public enum TranscriptEntryType
{
    USER_INPUT,
    MODEL_RESPONSE,
    THINKING,
    TOOL_CALL,
    TOOL_RESULT,
    SYSTEM_NOTICE,
    CONTEXT_CHECKPOINT
}

public sealed class TranscriptEntry
{
    [JsonPropertyName("step")]
    public int Step { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TranscriptEntryType Type { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    [JsonPropertyName("tool_args")]
    public string? ToolArgs { get; set; }

    [JsonPropertyName("tool_result")]
    public string? ToolResult { get; set; }
}

public sealed class AttachmentInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string RelativePath { get; set; } = "";
    public bool IsText { get; set; }
}

public enum SidecarTab
{
    Settings,
    Artifact,
    GitDiff
}

public sealed class DiffLine
{
    public string Type { get; set; } = "context"; // "add", "delete", "context", "header"
    public string Content { get; set; } = "";
    public int? OldLineNumber { get; set; }
    public int? NewLineNumber { get; set; }
}

public sealed class DiffFileItem
{
    public string FilePath { get; set; } = "";
    public string Status { get; set; } = "Modified"; // "Modified", "Added", "Deleted"
    public List<DiffLine> Lines { get; set; } = [];
    public bool IsAccepted { get; set; } = false;
    public bool IsRejected { get; set; } = false;
}

public sealed class ArtifactInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string FileName { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string RelativePath { get; set; } = "";
    public string? Language { get; set; }
}

public sealed class ChatStateService
{
    private string _activeChatTitle = "New chat";

    public string ActiveChatTitle
    {
        get => _activeChatTitle;
        set
        {
            if (_activeChatTitle != value)
            {
                _activeChatTitle = value;
                OnChange?.Invoke();
            }
        }
    }

    public event Action? OnChange;
    public event Func<string, Task>? OnTitleRenamed;

    public async Task RenameActiveChatAsync(string newTitle)
    {
        ActiveChatTitle = newTitle;
        if (OnTitleRenamed != null)
        {
            await OnTitleRenamed.Invoke(newTitle);
        }
    }
}
