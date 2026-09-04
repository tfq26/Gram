using System.Text.Json.Nodes;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class AgentToolsExecutionTests
{
    private (AgentToolRegistry registry, AgentSecurityService security, TemporaryDirectory tempDir) CreateTestContext(ExecutionPermissionMode mode = ExecutionPermissionMode.FullAutonomous)
    {
        var tempDir = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempDir.Path);

        var storage = new StorageService();
        var gitService = new GitService(storage);
        var securityService = new AgentSecurityService(storage);
        securityService.Settings.PermissionMode = mode;
        
        var toolboxService = new ToolboxManagerService(storage);
        var toolKernel = new GramToolKernel(securityService, toolboxService, Enumerable.Empty<IGramTool>());
        var webContent = new WebContentService(new SimpleFactory());

        var registry = new AgentToolRegistry(
            gitService,
            securityService,
            toolboxService,
            toolKernel,
            webContent);

        return (registry, securityService, tempDir);
    }

    [Fact]
    public async Task AgentTools_WriteFile_CreatesFileWithContent()
    {
        var (registry, _, tempDir) = CreateTestContext();
        using (tempDir)
        {
            var relativePath = "src/hello.cs";
            var content = "public class Hello { public static void World() => System.Console.WriteLine(\"Hi\"); }";

            var args = new JsonObject
            {
                ["filePath"] = relativePath,
                ["content"] = content
            }.ToJsonString();

            var result = await registry.ExecuteToolAsync("write_file", "call-write-1", args, tempDir.Path);

            Assert.False(result.IsError, result.Output);
            Assert.Contains("Successfully wrote", result.Output);

            var writtenPath = Path.Combine(tempDir.Path, relativePath);
            Assert.True(File.Exists(writtenPath));
            var savedText = await File.ReadAllTextAsync(writtenPath);
            Assert.Equal(content, savedText);
        }
    }

    [Fact]
    public async Task AgentTools_EditFile_UpdatesContentSuccessfully()
    {
        var (registry, _, tempDir) = CreateTestContext();
        using (tempDir)
        {
            var relativePath = "greeting.txt";
            var original = "Line 1\nHello World\nLine 3";
            var filePath = Path.Combine(tempDir.Path, relativePath);
            await File.WriteAllTextAsync(filePath, original);

            var editArgs = new JsonObject
            {
                ["filePath"] = relativePath,
                ["targetContent"] = "Hello World",
                ["replacementContent"] = "Hello Gram Autonomous Engine"
            }.ToJsonString();

            var result = await registry.ExecuteToolAsync("edit_file", "call-edit-1", editArgs, tempDir.Path);

            Assert.False(result.IsError, result.Output);
            Assert.Contains("Successfully edited", result.Output);

            var updatedText = await File.ReadAllTextAsync(filePath);
            Assert.Contains("Hello Gram Autonomous Engine", updatedText);
            Assert.DoesNotContain("Hello World", updatedText);
        }
    }

    [Fact]
    public async Task AgentTools_EditFile_FailsWhenTargetContentNotFound()
    {
        var (registry, _, tempDir) = CreateTestContext();
        using (tempDir)
        {
            var relativePath = "test.txt";
            var original = "Foo\nBar\nBaz";
            var filePath = Path.Combine(tempDir.Path, relativePath);
            await File.WriteAllTextAsync(filePath, original);

            var editArgs = new JsonObject
            {
                ["filePath"] = relativePath,
                ["targetContent"] = "NonExistentLine",
                ["replacementContent"] = "Replacement"
            }.ToJsonString();

            var result = await registry.ExecuteToolAsync("edit_file", "call-edit-2", editArgs, tempDir.Path);

            Assert.True(result.IsError);
            Assert.Contains("Target content was not found", result.Output);
        }
    }

    [Fact]
    public async Task AgentTools_RunCommand_ExecutesAndCapturesStdout()
    {
        var (registry, _, tempDir) = CreateTestContext();
        using (tempDir)
        {
            var cmdArgs = new JsonObject
            {
                ["command"] = "echo 'Gram Coding Engine Active'"
            }.ToJsonString();

            var result = await registry.ExecuteToolAsync("run_command", "call-cmd-1", cmdArgs, tempDir.Path);

            Assert.False(result.IsError, result.Output);
            Assert.Contains("Gram Coding Engine Active", result.Output);
        }
    }

    [Fact]
    public async Task AgentTools_RunCommand_BlocksDangerousCommands()
    {
        var (registry, _, tempDir) = CreateTestContext();
        using (tempDir)
        {
            var cmdArgs = new JsonObject
            {
                ["command"] = "rm -rf /"
            }.ToJsonString();

            var result = await registry.ExecuteToolAsync("run_command", "call-cmd-danger", cmdArgs, tempDir.Path);

            Assert.True(result.IsError);
            Assert.Contains("Security Policy", result.Output);
        }
    }

    [Fact]
    public async Task AgentSecurityService_InteractiveApproval_WorksAsync()
    {
        var (registry, securityService, tempDir) = CreateTestContext(ExecutionPermissionMode.AskAlways);
        using (tempDir)
        {
            // Subscribe to permission event and approve immediately in background
            securityService.OnPermissionRequested += req =>
            {
                Task.Run(() =>
                {
                    securityService.RespondApproval(req.Id, approved: true, rememberForSession: false);
                });
            };

            var cmdArgs = new JsonObject
            {
                ["command"] = "echo 'Approved by user'"
            }.ToJsonString();

            var result = await registry.ExecuteToolAsync("run_command", "call-cmd-approved", cmdArgs, tempDir.Path);

            Assert.False(result.IsError, result.Output);
            Assert.Contains("Approved by user", result.Output);
        }
    }

    [Fact]
    public async Task AgentSecurityService_InteractiveDenial_AbortsExecution()
    {
        var (registry, securityService, tempDir) = CreateTestContext(ExecutionPermissionMode.AskAlways);
        using (tempDir)
        {
            // Subscribe to permission event and deny immediately
            securityService.OnPermissionRequested += req =>
            {
                Task.Run(() =>
                {
                    securityService.RespondApproval(req.Id, approved: false, rememberForSession: false);
                });
            };

            var cmdArgs = new JsonObject
            {
                ["command"] = "echo 'Should not run'"
            }.ToJsonString();

            var result = await registry.ExecuteToolAsync("run_command", "call-cmd-denied", cmdArgs, tempDir.Path);

            Assert.True(result.IsError);
            Assert.Contains("denied by user", result.Output);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gram-test-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { }
        }
    }

    private sealed class SimpleFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
