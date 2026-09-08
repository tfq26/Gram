namespace Gram.Blazor.Shared;

public enum WorkspaceTabType
{
    Chat,
    ToolboxOverview,
    ToolConfig,
    Presentation,
    Terminal
}

public sealed class WorkspaceTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "Chat";
    public WorkspaceTabType Type { get; set; } = WorkspaceTabType.Chat;
    public string? ChatId { get; set; }
    public string? ToolId { get; set; }
    public string? ProjectId { get; set; }
    public string? PresentationPath { get; set; }
    public string? Icon { get; set; } = "💬";
    public bool IsPinned { get; set; }
}

public sealed class ToolboxToolInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "General"; // "Sandbox", "Code", "MCP", "Skills", "Git", "Browser"
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "⚡";
    public bool IsInstalled { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public string Status { get; set; } = "Ready";
    public string Version { get; set; } = "1.0.0";
}

public sealed class McpServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Transport { get; set; } = "stdio"; // "stdio", "sse"
    public string Command { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Url { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public bool IsConnected { get; set; }
    public int ToolCount { get; set; }
}

public sealed class AgentSkillDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "🧠";
    public string SystemPrompt { get; set; } = "";
    public List<string> PreferredTools { get; set; } = [];
    public bool IsActive { get; set; } = true;
}
