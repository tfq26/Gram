using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public enum SubagentTaskStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class SubagentDefinition
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Description { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public List<string> Capabilities { get; set; } = [];
    public List<string> AllowedToolNames { get; set; } = [];
    public bool UseGramBoxSandbox { get; set; }
    public string? ModelId { get; set; }
}

public sealed class SubagentTaskSpec
{
    public string ParentConversationId { get; set; } = "";
    public string Capability { get; set; } = "";
    public string TaskType { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string WorkspacePath { get; set; } = "";
    public string? SectionId { get; set; }
    public string? OutputFormat { get; set; }
    public string? PreferredProfileId { get; set; }
    public string? RoleOverride { get; set; }
    public bool? UseSandbox { get; set; }
    public string? ModelId { get; set; }
    public List<string> Dependencies { get; set; } = [];
}

public sealed class SubagentTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string ParentConversationId { get; set; } = "";
    public string SubagentName { get; set; } = "";
    public string Role { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string WorkspacePath { get; set; } = "";
    public string? SandboxId { get; set; }
    public string? SandboxPath { get; set; }
    public string Capability { get; set; } = "";
    public string TaskType { get; set; } = "";
    public string? SectionId { get; set; }
    public string? OutputFormat { get; set; }
    public List<string> Dependencies { get; set; } = [];
    public SubagentTaskStatus Status { get; set; } = SubagentTaskStatus.Queued;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ResultSummary { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> ModifiedFiles { get; set; } = [];
    public string? WorktreeDiff { get; set; }
    public int IterationsUsed { get; set; }
    public long DurationMs { get; set; }
    public List<string> ExecutionLogs { get; set; } = [];
}

public sealed class SubagentExecutionResult
{
    public string TaskId { get; set; } = "";
    public string SubagentName { get; set; } = "";
    public string Role { get; set; } = "";
    public bool Success { get; set; }
    public string Summary { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? SandboxPath { get; set; }
    public string? WorktreeDiff { get; set; }
    public List<string> ModifiedFiles { get; set; } = [];
    public long DurationMs { get; set; }
}
