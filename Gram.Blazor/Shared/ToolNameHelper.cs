namespace Gram.Blazor.Shared;

public static class ToolNameHelper
{
    private static readonly Dictionary<string, string> ToolDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // File & Codebase Operations
        ["view_file"] = "Read File",
        ["read_file"] = "Read File",
        ["get_file"] = "Read File",
        ["write_file"] = "Create File",
        ["write_to_file"] = "Create File",
        ["edit_file"] = "Edit File",
        ["replace_file_content"] = "Edit File",
        ["list_directory"] = "List Directory",
        ["list_dir"] = "List Directory",
        ["find_files"] = "Find Files",
        ["find_by_name"] = "Find Files",
        ["grep_search"] = "Search Codebase",
        ["search_codebase"] = "Search Codebase",

        // Terminal & Commands
        ["run_command"] = "Run Command",
        ["bash"] = "Run Command",
        ["exec"] = "Run Command",
        ["shell"] = "Run Command",
        ["terminal"] = "Run Command",

        // Web & Research
        ["search_web"] = "Search Web",
        ["web_search"] = "Search Web",
        ["fetch_web_content"] = "Read Web Page",
        ["read_url_content"] = "Read Web Page",
        ["browse_page"] = "Read Web Page",

        // Presentation & Decks
        ["create_presentation_project"] = "Create Presentation",
        ["create_presentation"] = "Create Presentation",
        ["revise_presentation_section"] = "Revise Slide",
        ["get_presentation_status"] = "Check Presentation Status",
        ["get_presentation_logs"] = "Read Presentation Logs",
        ["assemble_presentation_html"] = "Assemble Presentation",

        // Subagents
        ["spawn_subagent"] = "Launch Subagent",
        ["invoke_subagent"] = "Launch Subagent",
        ["get_subagent_status"] = "Check Subagent Status",
        ["list_subagents"] = "List Subagents",
        ["manage_subagents"] = "Manage Subagents",
        ["define_subagent"] = "Define Subagent",

        // GramBox / Worktree Sandboxes
        ["grambox_create_sandbox"] = "Create Sandbox",
        ["grambox_exec"] = "Run in Sandbox",
        ["grambox_list_sandboxes"] = "List Sandboxes",
        ["grambox_write_file"] = "Write Sandbox File",
        ["grambox_read_file"] = "Read Sandbox File",
        ["grambox_get_diff"] = "Inspect Sandbox Diff",
        ["grambox_commit_sandbox"] = "Commit Sandbox",
        ["grambox_preview_merge"] = "Preview Merge",
        ["grambox_close_sandbox"] = "Close Sandbox",
        ["grambox_checkpoint"] = "Create Checkpoint",
        ["grambox_rollback"] = "Rollback Checkpoint",
        ["grambox_status"] = "Sandbox Status",

        // Git & Version Control
        ["git_list_repos"] = "List Git Repositories",
        ["git_read_file"] = "Read Git File",
        ["git_commit_file"] = "Commit Git Changes",
        ["git_create_pr"] = "Create Pull Request",
        ["git_clone"] = "Clone Repository",
        ["git_checkout"] = "Checkout Branch",
        ["git_status"] = "Git Status",
        ["git_pull"] = "Pull Changes",
        ["git_push"] = "Push Changes",

        // Tasks, Memory & Scheduling
        ["manage_tasks"] = "Manage Tasks",
        ["remember_rule"] = "Save Memory Rule",
        ["set_reminder"] = "Set Reminder",
        ["schedule"] = "Schedule Task",
        ["ask_question"] = "Ask Question",

        // Toolbox & MCP
        ["toolbox_list_tools"] = "List Tools",
        ["toolbox_execute_tool"] = "Execute Tool",
        ["generate_image"] = "Generate Image"
    };

    public static string GetDisplayName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return "Tool Execution";

        var clean = toolName.Trim();
        if (ToolDisplayNames.TryGetValue(clean, out var displayName))
            return displayName;

        // Strip default_api prefix if present
        if (clean.StartsWith("default_api:", StringComparison.OrdinalIgnoreCase))
            clean = clean["default_api:".Length..];

        if (ToolDisplayNames.TryGetValue(clean, out var directMatch))
            return directMatch;

        // Smart Title Case formatting for any custom or dynamic MCP tool
        var words = clean
            .Replace("_", " ")
            .Replace("-", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
            return "Tool Execution";

        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w[1..].ToLowerInvariant() : "")));
    }
}
