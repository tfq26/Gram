using System.Text.Json.Nodes;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class WorkspaceRulesAndAnchoredEditingTests
{
    private sealed class SimpleFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static AgentToolRegistry CreateRegistry(string tempPath)
    {
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempPath);
        var storage = new StorageService();
        var gitService = new GitService(storage);
        var securityService = new AgentSecurityService(storage);
        securityService.Settings.PermissionMode = ExecutionPermissionMode.FullAutonomous;
        
        var toolboxService = new ToolboxManagerService(storage);
        var toolKernel = new GramToolKernel(securityService, toolboxService, Enumerable.Empty<IGramTool>());
        var webContent = new WebContentService(new SimpleFactory());

        return new AgentToolRegistry(
            gitService,
            securityService,
            toolboxService,
            toolKernel,
            webContent);
    }

    [Fact]
    public async Task DiscoverWorkspaceRules_ReadsGramMd_WhenPresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gram-rules-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var rulesContent = "# Project Rules\n- Always use async/await\n- No emoji in UI";
            await File.WriteAllTextAsync(Path.Combine(tempDir, "GRAM.md"), rulesContent);

            var discovered = await AgentOrchestrator.DiscoverWorkspaceRulesAsync(tempDir);

            Assert.NotNull(discovered);
            Assert.Equal(rulesContent, discovered);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task DiscoverWorkspaceRules_ReadsCursorRules_WhenGramMdAbsent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gram-rules-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var rulesContent = "Use Tailwind CSS and TypeScript strict mode.";
            await File.WriteAllTextAsync(Path.Combine(tempDir, ".cursorrules"), rulesContent);

            var discovered = await AgentOrchestrator.DiscoverWorkspaceRulesAsync(tempDir);

            Assert.NotNull(discovered);
            Assert.Equal(rulesContent, discovered);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AgentTools_EditFile_WithLineAnchors_SurgicallyModifiesTargetLines()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gram-edit-anchors-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var lines = new List<string>();
            for (int i = 1; i <= 20; i++)
            {
                if (i == 4 || i == 14)
                    lines.Add("    var handler = new Handler();");
                else
                    lines.Add($"    int val_{i} = {i};");
            }
            var filePath = "SampleService.cs";
            await File.WriteAllTextAsync(Path.Combine(tempDir, filePath), string.Join("\n", lines));

            var registry = CreateRegistry(tempDir);
            var args = new JsonObject
            {
                ["filePath"] = filePath,
                ["targetContent"] = "var handler = new Handler();",
                ["replacementContent"] = "var handler = new CustomHandler();",
                ["startLine"] = 10,
                ["endLine"] = 18
            }.ToJsonString();

            var result = await registry.ExecuteToolAsync("edit_file", "call-anchor-1", args, tempDir);

            Assert.False(result.IsError, result.Output);
            Assert.Contains("Successfully edited SampleService.cs", result.Output);

            var modifiedText = await File.ReadAllTextAsync(Path.Combine(tempDir, filePath));
            var modifiedLines = modifiedText.Split('\n');

            // Line 4 (index 3) must be untouched
            Assert.Contains("var handler = new Handler();", modifiedLines[3]);
            // Line 14 (index 13) must be modified
            Assert.Contains("var handler = new CustomHandler();", modifiedLines[13]);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AgentTools_EditFile_FailureReturnsActionableDiagnosticContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gram-edit-diag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var content = "public class OrderProcessor\n{\n    public async Task Execute(Order order)\n    {\n        Save(order);\n    }\n}";
            var filePath = "OrderProcessor.cs";
            await File.WriteAllTextAsync(Path.Combine(tempDir, filePath), content);

            var registry = CreateRegistry(tempDir);
            // Provide target content with a deliberate slight typo
            var args = new JsonObject
            {
                ["filePath"] = filePath,
                ["targetContent"] = "public async Task Execute(Order mismatchedOrder)",
                ["replacementContent"] = "public async Task Execute(Order order) { }"
            }.ToJsonString();

            var result = await registry.ExecuteToolAsync("edit_file", "call-diag-1", args, tempDir);

            Assert.True(result.IsError);
            Assert.Contains("Closest match found near line", result.Output);
            Assert.Contains("Actionable hint:", result.Output);
            Assert.Contains("OrderProcessor.cs", result.Output);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
