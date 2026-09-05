using System.Text.Json.Nodes;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class WorkspaceSecurityAndConfinementTests
{
    private (AgentToolRegistry registry, AgentSecurityService security, WorkspaceCheckpointService checkpoint, TemporaryDirectory tempDir) CreateTestContext(ExecutionPermissionMode mode = ExecutionPermissionMode.FullAutonomous)
    {
        var tempDir = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempDir.Path);

        var storage = new StorageService();
        var gitService = new GitService(storage);
        var securityService = new AgentSecurityService(storage);
        securityService.Settings.PermissionMode = mode;
        securityService.Settings.EnforceWorkspaceSandboxing = true;

        var checkpointService = new WorkspaceCheckpointService();
        var processRunner = new NativeProcessRunner();
        var toolboxService = new ToolboxManagerService(storage);
        var toolKernel = new GramToolKernel(securityService, toolboxService, Enumerable.Empty<IGramTool>());
        var webContent = new WebContentService(new SimpleHttpClientFactory());

        var registry = new AgentToolRegistry(
            gitService: gitService,
            securityService: securityService,
            toolboxService: toolboxService,
            toolKernel: toolKernel,
            webContentService: webContent,
            checkpointService: checkpointService,
            processRunner: processRunner);

        return (registry, securityService, checkpointService, tempDir);
    }

    [Fact]
    public async Task ViewFile_TraversingOutsideWorkspace_IsBlocked()
    {
        var (registry, _, _, tempDir) = CreateTestContext();
        using (tempDir)
        {
            var args = new JsonObject { ["filePath"] = "../outside.txt" }.ToJsonString();
            var result = await registry.ExecuteToolAsync("view_file", "call-view-escape", args, tempDir.Path);

            Assert.True(result.IsError);
            Assert.Contains("escapes workspace root", result.Output);
        }
    }

    [Fact]
    public async Task ListDirectory_TraversingOutsideWorkspace_IsBlocked()
    {
        var (registry, _, _, tempDir) = CreateTestContext();
        using (tempDir)
        {
            var args = new JsonObject { ["directoryPath"] = "../../" }.ToJsonString();
            var result = await registry.ExecuteToolAsync("list_directory", "call-list-escape", args, tempDir.Path);

            Assert.True(result.IsError);
            Assert.Contains("escapes workspace root", result.Output);
        }
    }

    [Fact]
    public async Task FindFiles_TraversingOutsideWorkspace_IsBlocked()
    {
        var (registry, _, _, tempDir) = CreateTestContext();
        using (tempDir)
        {
            var args = new JsonObject
            {
                ["pattern"] = "*.secret",
                ["directoryPath"] = "../"
            }.ToJsonString();
            var result = await registry.ExecuteToolAsync("find_files", "call-find-escape", args, tempDir.Path);

            Assert.True(result.IsError);
            Assert.Contains("escapes workspace root", result.Output);
        }
    }

    [Fact]
    public async Task WriteFile_AndRollback_RestoresOriginalState()
    {
        var (registry, _, checkpointService, tempDir) = CreateTestContext();
        using (tempDir)
        {
            var filePath = "app.config";
            var fullPath = Path.Combine(tempDir.Path, filePath);
            var originalContent = "setting=original";
            await File.WriteAllTextAsync(fullPath, originalContent);

            // Step 1: Write new content
            var writeArgs = new JsonObject
            {
                ["filePath"] = filePath,
                ["content"] = "setting=modified_by_agent"
            }.ToJsonString();

            var writeResult = await registry.ExecuteToolAsync("write_file", "call-w1", writeArgs, tempDir.Path);
            Assert.False(writeResult.IsError);
            Assert.Contains("Checkpoint:", writeResult.Output);

            var modifiedText = await File.ReadAllTextAsync(fullPath);
            Assert.Equal("setting=modified_by_agent", modifiedText);

            // Step 2: Rollback latest checkpoint
            var rollbackArgs = new JsonObject().ToJsonString();
            var rollbackResult = await registry.ExecuteToolAsync("rollback_checkpoint", "call-rb1", rollbackArgs, tempDir.Path);
            Assert.False(rollbackResult.IsError);
            Assert.Contains("Successfully rolled back", rollbackResult.Output);

            var restoredText = await File.ReadAllTextAsync(fullPath);
            Assert.Equal(originalContent, restoredText);
        }
    }

    [Fact]
    public async Task WriteFile_NewFile_RollbackDeletesIt()
    {
        var (registry, _, _, tempDir) = CreateTestContext();
        using (tempDir)
        {
            var filePath = "new_file.txt";
            var fullPath = Path.Combine(tempDir.Path, filePath);

            var writeArgs = new JsonObject
            {
                ["filePath"] = filePath,
                ["content"] = "temporary created content"
            }.ToJsonString();

            var writeResult = await registry.ExecuteToolAsync("write_file", "call-w2", writeArgs, tempDir.Path);
            Assert.False(writeResult.IsError);
            Assert.True(File.Exists(fullPath));

            // Rollback
            var rollbackResult = await registry.ExecuteToolAsync("rollback_checkpoint", "call-rb2", "{}", tempDir.Path);
            Assert.False(rollbackResult.IsError);
            Assert.False(File.Exists(fullPath)); // file should be removed
        }
    }

    [Fact]
    public async Task EditFile_CreatesCheckpoint_AndRollbackRevertsEdit()
    {
        var (registry, _, _, tempDir) = CreateTestContext();
        using (tempDir)
        {
            var filePath = "code.cs";
            var fullPath = Path.Combine(tempDir.Path, filePath);
            var original = "int x = 10;\nint y = 20;";
            await File.WriteAllTextAsync(fullPath, original);

            var editArgs = new JsonObject
            {
                ["filePath"] = filePath,
                ["targetContent"] = "int y = 20;",
                ["replacementContent"] = "int y = 999;"
            }.ToJsonString();

            var editResult = await registry.ExecuteToolAsync("edit_file", "call-e1", editArgs, tempDir.Path);
            Assert.False(editResult.IsError);
            Assert.Contains("Checkpoint:", editResult.Output);

            var editedContent = await File.ReadAllTextAsync(fullPath);
            Assert.Contains("int y = 999;", editedContent);

            // Rollback
            var rollbackResult = await registry.ExecuteToolAsync("rollback_checkpoint", "call-rb3", "{}", tempDir.Path);
            Assert.False(rollbackResult.IsError);

            var revertedContent = await File.ReadAllTextAsync(fullPath);
            Assert.Equal(original, revertedContent);
        }
    }
}
