using System.Diagnostics;
using System.Text.Json.Nodes;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.Tests;

public sealed class GitGramIntegrationTests
{
    [Fact]
    public async Task GitGramCanInspectStageAndCommitARealRepository()
    {
        await using var fixture = await TestRepository.CreateAsync();
        var service = new GitGramService(new SonnerService());

        var initial = await service.ExecuteControlledActionAsync("status", null, fixture.Path);

        Assert.True(initial.Success, initial.Output);
        Assert.Equal("main", initial.Data["branch"]?.ToString());
        Assert.True(bool.Parse(initial.Data["isClean"]?.ToString() ?? "false"));

        await File.AppendAllTextAsync(Path.Combine(fixture.Path, "README.md"), "\nchanged by integration test\n");

        var staged = await service.ExecuteControlledActionAsync("stage_file", "README.md", fixture.Path);
        Assert.True(staged.Success, staged.Output);

        var committed = await service.ExecuteControlledActionAsync("commit", "test: verify git gram", fixture.Path);
        Assert.True(committed.Success, committed.Output);

        var log = await fixture.RunGitAsync("log", "-1", "--pretty=%s");
        Assert.Equal("test: verify git gram", log.Trim());

        var final = await service.ExecuteControlledActionAsync("status", null, fixture.Path);
        Assert.True(final.Success, final.Output);
        Assert.True(bool.Parse(final.Data["isClean"]?.ToString() ?? "false"));
    }

    [Fact]
    public async Task GitGramRejectsWorkspacePathTraversal()
    {
        await using var fixture = await TestRepository.CreateAsync();
        var service = new GitGramService(new SonnerService());

        var result = await service.ExecuteControlledActionAsync("stage_file", "../outside.txt", fixture.Path);

        Assert.False(result.Success);
        Assert.Equal("git_operation_failed", result.ErrorCode);
        Assert.DoesNotContain("outside.txt", await fixture.RunGitAsync("status", "--short"));
    }

    [Fact]
    public async Task KernelRoutesGitGramAndReturnsStructuredMetadata()
    {
        await using var fixture = await TestRepository.CreateAsync();
        await using var data = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", data.Path);

        var storage = new StorageService();
        var security = new AgentSecurityService(storage);
        var toolbox = new ToolboxManagerService(storage);
        var gitGram = new GitGramService(new SonnerService());
        var kernel = new GramToolKernel(security, toolbox, new IGramTool[] { new GitGramKernelTool(gitGram) });

        var manifests = kernel.GetTools();
        Assert.Contains(manifests, manifest => manifest.Id == "git-gram" && manifest.MutatesRepository);
        Assert.Contains(manifests, manifest => manifest.ExpectedLatencyMs > 0);

        var result = await kernel.ExecuteAsync(
            "git-gram",
            "status",
            new JsonObject(),
            new GramToolExecutionContext
            {
                WorkspacePath = fixture.Path,
                RepositoryPath = fixture.Path,
                ApprovalGranted = true
            });

        Assert.True(result.IsSuccess, result.Output);
        Assert.Equal("main", result.Data["branch"]?.ToString());

        var unknown = await kernel.ExecuteAsync(
            "not-installed",
            "run",
            new JsonObject(),
            new GramToolExecutionContext { WorkspacePath = fixture.Path });
        Assert.Equal(GramToolResultStatus.Failed, unknown.Status);
        Assert.Equal("tool_not_found", unknown.ErrorCode);
    }

    [Fact]
    public async Task KernelRequiresApprovalForRepositoryMutationInAskMode()
    {
        await using var fixture = await TestRepository.CreateAsync();
        await using var data = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", data.Path);

        var storage = new StorageService();
        var security = new AgentSecurityService(storage);
        security.Settings.PermissionMode = ExecutionPermissionMode.AskAlways;
        var toolbox = new ToolboxManagerService(storage);
        var kernel = new GramToolKernel(security, toolbox, new IGramTool[]
        {
            new GitGramKernelTool(new GitGramService(new SonnerService()))
        });

        var result = await kernel.ExecuteAsync(
            "git-gram",
            "commit",
            new JsonObject { ["payload"] = "test: should require approval" },
            new GramToolExecutionContext
            {
                WorkspacePath = fixture.Path,
                RepositoryPath = fixture.Path,
                ApprovalGranted = false
            });

        Assert.Equal(GramToolResultStatus.ApprovalRequired, result.Status);
        Assert.Equal("approval_required", result.ErrorCode);
        Assert.Contains("approval", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkflowRunsIndependentReadOnlyToolsInParallel()
    {
        await using var data = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", data.Path);
        var storage = new StorageService();
        var security = new AgentSecurityService(storage);
        var toolbox = new ToolboxManagerService(storage);
        var first = new DelayedReadTool("read-one");
        var second = new DelayedReadTool("read-two");
        var kernel = new GramToolKernel(security, toolbox, new IGramTool[] { first, second });

        var stopwatch = Stopwatch.StartNew();
        var workflow = await kernel.ExecuteWorkflowAsync(
            new GramWorkflowDefinition
            {
                Id = "parallel-read-test",
                Steps = new[]
                {
                    new GramWorkflowStep { Id = "one", ToolId = "read-one", Action = "inspect" },
                    new GramWorkflowStep { Id = "two", ToolId = "read-two", Action = "inspect" }
                }
            },
            new GramToolExecutionContext { WorkspacePath = data.Path });
        stopwatch.Stop();

        Assert.True(workflow.IsSuccess);
        Assert.Equal(2, workflow.StepResults.Count);
        Assert.True(stopwatch.ElapsedMilliseconds < 350, $"Expected parallel execution, took {stopwatch.ElapsedMilliseconds}ms.");
    }

    private sealed class DelayedReadTool : IGramTool
    {
        public GramToolManifest Manifest { get; }

        public DelayedReadTool(string id)
        {
            Manifest = new GramToolManifest
            {
                Id = id,
                Name = id,
                Description = "Test read-only tool",
                Kind = GramToolKind.Native,
                SupportsParallelExecution = true,
                Capabilities = new[] { "workspace.read" },
                ExpectedLatencyMs = 150
            };
        }

        public async Task<GramToolResult> ExecuteAsync(GramToolExecutionRequest request)
        {
            await Task.Delay(150, request.CancellationToken);
            return GramToolResult.Completed(Manifest.Id, request.Action, "ok");
        }
    }

    private sealed class TestRepository : TemporaryDirectory
    {
        private TestRepository(string path) : base(path) { }

        public static async Task<TestRepository> CreateAsync()
        {
            var directory = new TestRepository(CreatePath());
            await directory.RunGitAsync("init", "-q", "-b", "main");
            await directory.RunGitAsync("config", "user.email", "gram-test@example.invalid");
            await directory.RunGitAsync("config", "user.name", "Gram Integration Test");
            await File.WriteAllTextAsync(System.IO.Path.Combine(directory.Path, "README.md"), "initial\n");
            await directory.RunGitAsync("add", "README.md");
            await directory.RunGitAsync("commit", "-q", "-m", "chore: initial test commit");
            return directory;
        }

        public async Task<string> RunGitAsync(params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = Path,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {stderr}");
            return stdout;
        }
    }

    private class TemporaryDirectory : IAsyncDisposable
    {
        public string Path { get; }

        protected TemporaryDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public TemporaryDirectory() : this(CreatePath()) { }

        protected static string CreatePath() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gram-tests-{Guid.NewGuid():N}");

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
