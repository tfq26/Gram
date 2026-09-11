using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class WorkspaceDiagnosticServiceUnitTests
{
    [Fact]
    public void DetectToolchain_DotNet_DetectsCsproj()
    {
        using var tempDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "App.csproj"), "<Project />");

        var service = new WorkspaceDiagnosticService();
        var toolchain = service.DetectToolchain(tempDir.Path);

        Assert.Equal("dotnet", toolchain);
    }

    [Fact]
    public void DetectToolchain_Rust_DetectsCargoToml()
    {
        using var tempDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "Cargo.toml"), "[package]\nname = \"foo\"");

        var service = new WorkspaceDiagnosticService();
        var toolchain = service.DetectToolchain(tempDir.Path);

        Assert.Equal("cargo", toolchain);
    }

    [Fact]
    public void DetectToolchain_Go_DetectsGoMod()
    {
        using var tempDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "go.mod"), "module example.com/foo");

        var service = new WorkspaceDiagnosticService();
        var toolchain = service.DetectToolchain(tempDir.Path);

        Assert.Equal("go", toolchain);
    }

    [Fact]
    public void DetectToolchain_TypeScript_DetectsTsConfig()
    {
        using var tempDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "tsconfig.json"), "{}");

        var service = new WorkspaceDiagnosticService();
        var toolchain = service.DetectToolchain(tempDir.Path);

        Assert.Equal("typescript", toolchain);
    }

    [Fact]
    public void DetectToolchain_Python_DetectsPyProject()
    {
        using var tempDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "pyproject.toml"), "[build-system]");

        var service = new WorkspaceDiagnosticService();
        var toolchain = service.DetectToolchain(tempDir.Path);

        Assert.Equal("python", toolchain);
    }

    [Fact]
    public async Task VerifyAsync_EmptyWorkspace_ReturnsCleanNone()
    {
        using var tempDir = new TemporaryDirectory();
        var service = new WorkspaceDiagnosticService();

        var result = await service.VerifyAsync(tempDir.Path);

        Assert.True(result.Success);
        Assert.Equal("None", result.ToolchainName);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task VerifyAsync_MockFailingProcess_ReturnsDiagnosticsAndFailure()
    {
        using var tempDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "App.csproj"), "<Project />");

        var mockRunner = new MockProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 1,
            StandardOutput = "Program.cs(42,10): error CS1002: ; expected",
            StandardError = ""
        });

        var service = new WorkspaceDiagnosticService(mockRunner);
        var result = await service.VerifyAsync(tempDir.Path);

        Assert.False(result.Success);
        Assert.Equal("dotnet", result.ToolchainName);
        Assert.Single(result.Diagnostics);
        Assert.Equal("ERROR", result.Diagnostics[0].Severity);
        Assert.Equal("CS1002", result.Diagnostics[0].Code);
    }

    [Fact]
    public async Task VerifyAsync_MockSuccessProcess_ReturnsSuccess()
    {
        using var tempDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "Cargo.toml"), "[package]\nname = \"foo\"");

        var mockRunner = new MockProcessRunner(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "Finished dev [unoptimized + debuginfo] target(s) in 0.12s",
            StandardError = ""
        });

        var service = new WorkspaceDiagnosticService(mockRunner);
        var result = await service.VerifyAsync(tempDir.Path);

        Assert.True(result.Success);
        Assert.Equal("cargo", result.ToolchainName);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task WorkspaceCheckpointService_CaptureAndRollback_RestoresOriginalContent()
    {
        using var tempDir = new TemporaryDirectory();
        var relPath = "src/Greeter.cs";
        var fullPath = Path.Combine(tempDir.Path, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "Original content v1");

        var cpService = new WorkspaceCheckpointService();
        var cp = await cpService.CapturePreWriteSnapshotAsync(tempDir.Path, relPath, "editing file");

        // Overwrite file
        await File.WriteAllTextAsync(fullPath, "Modified content v2 with syntax error");

        // Roll back
        var rolledBack = await cpService.RollbackCheckpointAsync(cp.Id);
        Assert.True(rolledBack);

        var restoredContent = await File.ReadAllTextAsync(fullPath);
        Assert.Equal("Original content v1", restoredContent);
    }

    [Fact]
    public async Task WorkspaceCheckpointService_RollbackNewFile_DeletesCreatedFile()
    {
        using var tempDir = new TemporaryDirectory();
        var relPath = "NewFile.cs";
        var fullPath = Path.Combine(tempDir.Path, relPath);

        var cpService = new WorkspaceCheckpointService();
        var cp = await cpService.CapturePreWriteSnapshotAsync(tempDir.Path, relPath, "creating file");

        // Simulate agent creating the file
        await File.WriteAllTextAsync(fullPath, "brand new file content");
        Assert.True(File.Exists(fullPath));

        // Roll back
        var rolledBack = await cpService.RollbackCheckpointAsync(cp.Id);
        Assert.True(rolledBack);
        Assert.False(File.Exists(fullPath));
    }

    private sealed class MockProcessRunner : INativeProcessRunner
    {
        private readonly ProcessExecutionResult _result;

        public MockProcessRunner(ProcessExecutionResult result)
        {
            _result = result;
        }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionOptions options, CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }

        public Task<ProcessExecutionResult> RunRawProcessAsync(string fileName, string arguments, string workingDirectory, int timeoutSeconds, CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }
    }
}
