using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ToolSelectionPolicyUnitTests
{
    [Fact]
    public void ReadOnlyQuestionDoesNotExposeMutationOrSandboxTools()
    {
        var tools = Definitions("view_file", "find_files", "grep_search", "search_web", "write_file", "edit_file", "run_command", "toolbox_execute_tool");

        var selected = ToolSelectionPolicy.SelectForIntent(tools, "What does this project do?");

        Assert.Contains(selected, x => x.Name == "view_file");
        Assert.Contains(selected, x => x.Name == "search_web");
        Assert.DoesNotContain(selected, x => x.Name == "write_file");
        Assert.DoesNotContain(selected, x => x.Name == "toolbox_execute_tool");
    }

    [Fact]
    public void CodingTaskExposesMutationAndExecutionTools()
    {
        var tools = Definitions("view_file", "find_files", "grep_search", "search_web", "write_file", "edit_file", "run_command", "toolbox_execute_tool");

        var selected = ToolSelectionPolicy.SelectForIntent(tools, "Implement the audio visualizer and run the tests");

        Assert.Contains(selected, x => x.Name == "write_file");
        Assert.Contains(selected, x => x.Name == "edit_file");
        Assert.Contains(selected, x => x.Name == "run_command");
        Assert.DoesNotContain(selected, x => x.Name == "toolbox_execute_tool");
    }

    [Fact]
    public void ToolboxTaskExposesToolboxEntryPoint()
    {
        var tools = Definitions("view_file", "find_files", "grep_search", "search_web", "toolbox_list_tools", "toolbox_execute_tool", "grambox_status");

        var selected = ToolSelectionPolicy.SelectForIntent(tools, "Use the toolbox to inspect LSP diagnostics");

        Assert.Contains(selected, x => x.Name == "toolbox_list_tools");
        Assert.Contains(selected, x => x.Name == "toolbox_execute_tool");
        Assert.Contains(selected, x => x.Name == "grambox_status");
    }

    private static List<AgentToolDefinition> Definitions(params string[] names) => names
        .Select(name => new AgentToolDefinition { Name = name, Description = name })
        .ToList();
}
