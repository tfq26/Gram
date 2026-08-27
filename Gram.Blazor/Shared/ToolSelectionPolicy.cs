namespace Gram.Blazor.Shared;

public static class ToolSelectionPolicy
{
    private static readonly HashSet<string> AlwaysUseful = new(StringComparer.OrdinalIgnoreCase)
    {
        "view_file",
        "find_files",
        "grep_search",
        "search_web"
    };

    public static List<AgentToolDefinition> SelectForIntent(
        IEnumerable<AgentToolDefinition> availableTools,
        string? intent)
    {
        var tools = availableTools.ToList();
        var text = (intent ?? "").ToLowerInvariant();
        var isChangeTask = ContainsAny(text, "implement", "create", "add", "update", "fix", "refactor", "edit", "write", "build", "test", "run", "component", "feature");
        var isWebTask = ContainsAny(text, "web", "internet", "online", "documentation", "docs", "latest", "research", "search");
        var isGitTask = ContainsAny(text, "git", "repository", "repo", "branch", "commit", "push", "pull request", "merge", "diff");
        var isToolboxTask = ContainsAny(text, "toolbox", "sandbox", "ast", "tree-sitter", "lsp", "playwright", "browser", "verify", "diagnostic", "cde");
        var isPresentationTask = ContainsAny(text, "presentation", "powerpoint", "power point", "slide deck", "slides", "pptx");
        var allowed = new HashSet<string>(AlwaysUseful, StringComparer.OrdinalIgnoreCase);

        if (isWebTask)
        {
            allowed.Add("fetch_web_content");
        }

        if (isChangeTask)
        {
            allowed.UnionWith(["list_directory", "write_file", "edit_file", "run_command"]);
        }

        if (isGitTask)
        {
            allowed.UnionWith([
                "git_list_repos",
                "git_read_file",
                "git_commit_file",
                "git_create_pr"
            ]);
        }

        if (isToolboxTask)
        {
            allowed.UnionWith(["toolbox_list_tools", "toolbox_execute_tool"]);
            allowed.UnionWith(tools.Where(x => x.Name.StartsWith("grambox_", StringComparison.OrdinalIgnoreCase)).Select(x => x.Name));
        }

        if (isPresentationTask)
        {
            allowed.UnionWith(["create_presentation_project", "revise_presentation_section", "get_presentation_status", "get_presentation_logs", "spawn_subagent", "get_subagent_status", "list_subagents"]);
        }

        // A codebase question needs directory context but not mutation tools.
        if (isChangeTask || text.Contains("codebase") || text.Contains("project"))
        {
            allowed.Add("list_directory");
        }

        return tools
            .Where(tool => allowed.Contains(tool.Name))
            .ToList();
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(value.Contains);
}
