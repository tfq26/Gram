using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ToolStrippingSecurityTests
{
    [Theory]
    [InlineData("search_web", true)]
    [InlineData("fetch_web_content", true)]
    [InlineData("write_file", false)]
    [InlineData("edit_file", false)]
    [InlineData("run_command", false)]
    [InlineData("list_directory", false)]
    [InlineData("find_files", false)]
    [InlineData("grep_search", false)]
    [InlineData("grambox_create_sandbox", false)]
    [InlineData("grambox_exec", false)]
    [InlineData("db_query_readonly", false)]
    [InlineData("db_execute_dml", false)]
    public void IsAllowedInChatOnlyMode_OnlyPermitsReadOnlyInformationTools(string toolName, bool expectedAllowed)
    {
        var allowed = AgentOrchestrator.IsAllowedInChatOnlyMode(toolName);
        Assert.Equal(expectedAllowed, allowed);
    }

    [Fact]
    public void ChatOnlyModel_StripsAllMutatingToolsFromList()
    {
        var allTools = new List<AgentToolDefinition>
        {
            new() { Name = "search_web", Description = "Search" },
            new() { Name = "fetch_web_content", Description = "Fetch" },
            new() { Name = "write_file", Description = "Write" },
            new() { Name = "edit_file", Description = "Edit" },
            new() { Name = "run_command", Description = "Execute" },
            new() { Name = "grambox_exec", Description = "Grambox" }
        };

        var filtered = allTools.Where(t => AgentOrchestrator.IsAllowedInChatOnlyMode(t.Name)).ToList();

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, t => Assert.True(t.Name == "search_web" || t.Name == "fetch_web_content"));
        Assert.DoesNotContain(filtered, t => t.Name == "write_file");
        Assert.DoesNotContain(filtered, t => t.Name == "edit_file");
        Assert.DoesNotContain(filtered, t => t.Name == "run_command");
    }
}
