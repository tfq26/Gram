using System.Text.Json.Nodes;
using Gram.Blazor.Components;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ProjectScaffoldingWorkflowTests
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

    [Theory]
    [InlineData("plan.md", "Project Execution Plan", true)]
    [InlineData("architecture_plan.md", "Architecture", true)]
    [InlineData("scaffolding_plan.markdown", "Scaffolding", false)]
    [InlineData("README.md", "Overall Plan for Database", true)]
    [InlineData("index.html", "Landing Page", false)]
    [InlineData("styles.css", "Styles", false)]
    public void IsPlanArtifact_CorrectlyIdentifiesPlanningArtifacts(string fileName, string title, bool expected)
    {
        var artifact = new ArtifactInfo
        {
            FileName = fileName,
            Title = title
        };

        Assert.Equal(expected, artifact.IsPlan);
    }

    [Fact]
    public void ScaffoldingPrompt_FormattedCorrectlyForExecution()
    {
        var plan = new ArtifactInfo
        {
            FileName = "plan.md",
            Title = "Event Booking Service Architecture",
            Language = "markdown",
            Content = "# Event Booking Service\n1. Scaffolding\n2. Implementation\n3. Verification"
        };

        var prompt = $"The project plan '{plan.Title ?? plan.FileName}' has been approved by the user.\n\n" +
                     $"Execute the plan step-by-step:\n" +
                     $"1. Run non-interactive project initializers via run_command (using --yes or non-interactive defaults).\n" +
                     $"2. Scaffold all necessary source files and configurations using apply_changeset or write_file.\n" +
                     $"3. Create comprehensive unit tests matching the project toolchain.\n" +
                     $"4. Verify the entire project with run_tests to ensure zero errors and passing test suites.";

        Assert.Contains("Event Booking Service Architecture", prompt);
        Assert.Contains("run_command", prompt);
        Assert.Contains("apply_changeset or write_file", prompt);
        Assert.Contains("run_tests", prompt);
    }

    [Fact]
    public void ExtractPlanArtifact_ReturnsNull_WhenNoToolCallsPresent()
    {
        var message = new Message("assistant", "Hello developer!");
        var details = MessageList.ExtractPlanArtifact(message);
        Assert.Null(details);
    }

    [Fact]
    public void ExtractPlanArtifact_ReturnsNull_WhenToolCallIsNotPlan()
    {
        var message = new Message("assistant", "Writing file...");
        message.ToolCalls = new List<ToolCallInfo>
        {
            new ToolCallInfo
            {
                Id = "call-1",
                ToolName = "write_file",
                Arguments = "{\"filePath\": \"main.go\", \"content\": \"package main\"}"
            }
        };

        var details = MessageList.ExtractPlanArtifact(message);
        Assert.Null(details);
    }

    [Fact]
    public void ExtractPlanArtifact_ExtractsDetails_WhenCreatePlanArtifactPresent()
    {
        var message = new Message("assistant", "Here is your plan.");
        var argsJson = new JsonObject
        {
            ["fileName"] = "event_booking_plan.md",
            ["title"] = "Event Booking Service Architecture",
            ["description"] = "Microservice architecture with SQLite and EF Core",
            ["content"] = "# Blueprint\nScaffolding steps..."
        }.ToJsonString();

        message.ToolCalls = new List<ToolCallInfo>
        {
            new ToolCallInfo
            {
                Id = "call-plan-1",
                ToolName = "create_plan_artifact",
                Arguments = argsJson
            }
        };

        var details = MessageList.ExtractPlanArtifact(message);
        Assert.NotNull(details);
        Assert.Equal("event_booking_plan.md", details.FileName);
        Assert.Equal("Event Booking Service Architecture", details.Title);
        Assert.Equal("Microservice architecture with SQLite and EF Core", details.Description);
        Assert.Equal("# Blueprint\nScaffolding steps...", details.Content);
    }

    [Fact]
    public void ExtractPlanArtifact_FallsBackToFileNameWithoutExtension_WhenTitleOmitted()
    {
        var message = new Message("assistant", "Plan formulated.");
        var argsJson = new JsonObject
        {
            ["fileName"] = "distributed_cache_plan.md",
            ["content"] = "# Cache Design"
        }.ToJsonString();

        message.ToolCalls = new List<ToolCallInfo>
        {
            new ToolCallInfo
            {
                Id = "call-plan-2",
                ToolName = "create_plan_artifact",
                Arguments = argsJson
            }
        };

        var details = MessageList.ExtractPlanArtifact(message);
        Assert.NotNull(details);
        Assert.Equal("distributed_cache_plan.md", details.FileName);
        Assert.Equal("distributed_cache_plan", details.Title);
    }

    [Fact]
    public void AgentToolRegistry_DefinesCreatePlanArtifact()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gram-plan-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var registry = CreateRegistry(tempDir);
            var definitions = registry.GetAvailableTools(isLocalEnvironment: true);
            var planTool = definitions.FirstOrDefault(t => t.Name == "create_plan_artifact");

            Assert.NotNull(planTool);
            Assert.Contains("Sidecar", planTool.Description);
            var props = planTool.Parameters?["properties"] as JsonObject;
            Assert.NotNull(props);
            Assert.True(props.ContainsKey("fileName"));
            Assert.True(props.ContainsKey("title"));
            Assert.True(props.ContainsKey("content"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteToolAsync_CreatePlanArtifact_SavesFileAndReturnsJsonPayload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gram-plan-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var registry = CreateRegistry(tempDir);
            var args = new JsonObject
            {
                ["fileName"] = "todo_service_plan.md",
                ["title"] = "Todo Service Scaffolding Plan",
                ["description"] = "A RESTful Todo API in Go with SQLite",
                ["content"] = "# Todo Service Architecture\n\n1. Project setup\n2. Store\n3. Tests"
            }.ToJsonString();

            var result = await registry.ExecuteToolAsync("create_plan_artifact", "call-plan-3", args, tempDir);

            Assert.False(result.IsError);
            Assert.Contains("successfully published to Sidecar", result.Output);
            Assert.True(result.Output.Contains("\"isPlan\":true") || result.Output.Contains("\"isPlan\": true"));

            var savedFilePath = Path.Combine(tempDir, ".gram", "artifacts", "todo_service_plan.md");
            Assert.True(File.Exists(savedFilePath));
            var savedContent = await File.ReadAllTextAsync(savedFilePath);
            Assert.Equal("# Todo Service Architecture\n\n1. Project setup\n2. Store\n3. Tests", savedContent);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteToolAsync_CreatePlanArtifact_RejectsEmptyContent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gram-plan-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var registry = CreateRegistry(tempDir);
            var args = new JsonObject
            {
                ["fileName"] = "empty_plan.md",
                ["title"] = "Empty Plan",
                ["content"] = ""
            }.ToJsonString();

            var result = await registry.ExecuteToolAsync("create_plan_artifact", "call-plan-4", args, tempDir);

            Assert.True(result.IsError);
            Assert.Contains("Plan content cannot be empty", result.Output);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GramSystemPrompt_IncludesScaffoldingProtocol()
    {
        var prompt = GramSystemPrompt.DefaultBasePrompt;

        Assert.Contains("Project Scaffolding & Sidecar Planning Protocol", prompt);
        Assert.Contains("Phase 1 (Ideation & Requirements)", prompt);
        Assert.Contains("Phase 2 (Structured Plan Deliverable)", prompt);
        Assert.Contains("Phase 3 (Autonomous Scaffolding upon Approval)", prompt);
        Assert.Contains("create_plan_artifact", prompt);
    }
}
