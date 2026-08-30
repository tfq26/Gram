using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ToolKernelUnitTests
{
    [Fact]
    public void ToolSchemaCanBeAttachedToMultipleRequestPayloads()
    {
        var definition = new AgentToolDefinition
        {
            Name = "search_web",
            Description = "Search the web",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject { ["type"] = "string" }
                }
            }
        };

        var first = definition.ToOpenAiFunctionSchema();
        var second = definition.ToOpenAiFunctionSchema();

        Assert.NotNull(first["function"]?["parameters"]);
        Assert.NotNull(second["function"]?["parameters"]);
        Assert.Equal(first.ToJsonString(), second.ToJsonString());
    }

    [Fact]
    public async Task UnknownToolReturnsStructuredNotFoundResult()
    {
        using var fixture = new KernelFixture();

        var result = await fixture.Kernel.ExecuteAsync(
            "missing-tool",
            "run",
            new JsonObject(),
            fixture.Context());

        Assert.Equal(GramToolResultStatus.Failed, result.Status);
        Assert.Equal("tool_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task MissingCapabilityDeniesToolExecutionBeforeHandlerRuns()
    {
        using var fixture = new KernelFixture();
        var executed = false;
        fixture.Add(new TestTool("protected-tool", new[] { "repository.write" }, _ =>
        {
            executed = true;
            return Task.FromResult(GramToolResult.Completed("protected-tool", "run", "should not run"));
        }));

        var result = await fixture.Kernel.ExecuteAsync(
            "protected-tool",
            "run",
            new JsonObject(),
            fixture.Context(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "workspace.read" }));

        Assert.Equal(GramToolResultStatus.Denied, result.Status);
        Assert.Equal("tool_denied", result.ErrorCode);
        Assert.False(executed);
    }

    [Fact]
    public async Task AskAlwaysRequiresApprovalForMutatingTools()
    {
        using var fixture = new KernelFixture();
        fixture.Security.Settings.PermissionMode = ExecutionPermissionMode.AskAlways;
        fixture.Add(new TestTool("mutating-tool", Array.Empty<string>(), _ =>
            Task.FromResult(GramToolResult.Completed("mutating-tool", "run", "should not run")))
        {
            MutatesRepository = true,
            Risk = GramToolRisk.High
        });

        var result = await fixture.Kernel.ExecuteAsync(
            "mutating-tool",
            "run",
            new JsonObject(),
            fixture.Context());

        Assert.Equal(GramToolResultStatus.ApprovalRequired, result.Status);
        Assert.Equal("approval_required", result.ErrorCode);
    }

    [Fact]
    public async Task ApprovedMutatingToolCanExecuteInAskAlwaysMode()
    {
        using var fixture = new KernelFixture();
        fixture.Security.Settings.PermissionMode = ExecutionPermissionMode.AskAlways;
        fixture.Add(new TestTool("approved-tool", Array.Empty<string>(), _ =>
            Task.FromResult(GramToolResult.Completed("approved-tool", "run", "executed")))
        {
            MutatesRepository = true
        });

        var result = await fixture.Kernel.ExecuteAsync(
            "approved-tool",
            "run",
            new JsonObject(),
            fixture.Context(approvalGranted: true));

        Assert.True(result.IsSuccess);
        Assert.Equal("executed", result.Output);
    }

    [Fact]
    public async Task WorkflowRespectsDependenciesAndStopsAfterFailure()
    {
        using var fixture = new KernelFixture();
        var executed = new ConcurrentQueue<string>();
        fixture.Add(new TestTool("failing-step", Array.Empty<string>(), _ =>
        {
            executed.Enqueue("failing-step");
            return Task.FromResult(GramToolResult.Failed("failing-step", "run", "expected failure"));
        }));
        fixture.Add(new TestTool("dependent-step", Array.Empty<string>(), _ =>
        {
            executed.Enqueue("dependent-step");
            return Task.FromResult(GramToolResult.Completed("dependent-step", "run", "should not run"));
        }));

        var workflow = await fixture.Kernel.ExecuteWorkflowAsync(
            new GramWorkflowDefinition
            {
                Id = "failure-flow",
                Steps = new[]
                {
                    new GramWorkflowStep { Id = "first", ToolId = "failing-step" },
                    new GramWorkflowStep { Id = "second", ToolId = "dependent-step", DependsOn = new[] { "first" } }
                }
            },
            fixture.Context());

        Assert.False(workflow.IsSuccess);
        Assert.Single(workflow.StepResults);
        Assert.Equal("failing-step", Assert.Single(executed));
    }

    [Fact]
    public async Task WorkflowPassesDependencyResultsToFollowingTool()
    {
        using var fixture = new KernelFixture();
        fixture.Add(new TestTool("producer", Array.Empty<string>(), _ =>
            Task.FromResult(GramToolResult.Completed("producer", "run", "produced", new JsonObject { ["value"] = 42 }))));
        JsonObject? received = null;
        fixture.Add(new TestTool("consumer", Array.Empty<string>(), request =>
        {
            received = request.DependencyResults["first"].Data;
            return Task.FromResult(GramToolResult.Completed("consumer", "run", "consumed"));
        }));

        var workflow = await fixture.Kernel.ExecuteWorkflowAsync(
            new GramWorkflowDefinition
            {
                Id = "dependency-flow",
                Steps = new[]
                {
                    new GramWorkflowStep { Id = "first", ToolId = "producer" },
                    new GramWorkflowStep { Id = "second", ToolId = "consumer", DependsOn = new[] { "first" } }
                }
            },
            fixture.Context());

        Assert.True(workflow.IsSuccess);
        Assert.Equal("42", received?["value"]?.ToString());
    }

    private sealed class KernelFixture : IDisposable
    {
        private readonly List<IGramTool> tools = new();
        private readonly TemporaryDirectory dataDirectory = new();

        public AgentSecurityService Security { get; }
        public GramToolKernel Kernel { get; private set; }

        public KernelFixture()
        {
            Environment.SetEnvironmentVariable("GRAM_DATA_DIR", dataDirectory.Path);
            Security = new AgentSecurityService(new StorageService());
            Kernel = CreateKernel();
        }

        public void Add(IGramTool tool)
        {
            tools.Add(tool);
            Kernel = CreateKernel();
        }

        public GramToolExecutionContext Context(IReadOnlySet<string>? capabilities = null, bool approvalGranted = false) => new()
        {
            WorkspacePath = dataDirectory.Path,
            RepositoryPath = dataDirectory.Path,
            ApprovalGranted = approvalGranted,
            GrantedCapabilities = capabilities ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "workspace.read",
                "workspace.write",
                "repository.read",
                "repository.write",
                "sandbox.execute",
                "remote.network"
            }
        };

        private GramToolKernel CreateKernel()
        {
            var toolbox = new ToolboxManagerService(new StorageService());
            return new GramToolKernel(Security, toolbox, tools);
        }

        public void Dispose() => dataDirectory.Dispose();
    }

    private sealed class TestTool : IGramTool
    {
        private readonly Func<GramToolExecutionRequest, Task<GramToolResult>> execute;
        private readonly string id;
        private readonly IReadOnlyList<string> capabilities;
        public bool MutatesRepository { get; init; }
        public GramToolRisk Risk { get; init; } = GramToolRisk.Low;
        public GramToolManifest Manifest => new()
        {
            Id = id,
            Name = id,
            Description = "Unit test tool",
            Capabilities = capabilities,
            SupportsParallelExecution = !MutatesRepository,
            MutatesRepository = MutatesRepository,
            Risk = Risk
        };

        public TestTool(string id, IReadOnlyList<string> capabilities, Func<GramToolExecutionRequest, Task<GramToolResult>> execute)
        {
            this.id = id;
            this.capabilities = capabilities;
            this.execute = execute;
        }

        public Task<GramToolResult> ExecuteAsync(GramToolExecutionRequest request) => execute(request);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gram-unit-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    [Theory]
    [InlineData("view_file", "Read File")]
    [InlineData("write_to_file", "Create File")]
    [InlineData("edit_file", "Edit File")]
    [InlineData("replace_file_content", "Edit File")]
    [InlineData("run_command", "Run Command")]
    [InlineData("search_web", "Search Web")]
    [InlineData("fetch_web_content", "Read Web Page")]
    [InlineData("create_presentation_project", "Create Presentation")]
    [InlineData("revise_presentation_section", "Revise Slide")]
    [InlineData("spawn_subagent", "Launch Subagent")]
    [InlineData("grambox_create_sandbox", "Create Sandbox")]
    [InlineData("grambox_exec", "Run in Sandbox")]
    [InlineData("custom_mcp_tool", "Custom Mcp Tool")]
    public void ToolNameHelperReturnsFriendlyDisplayNames(string rawName, string expectedDisplay)
    {
        var result = ToolNameHelper.GetDisplayName(rawName);
        Assert.Equal(expectedDisplay, result);
    }
}
