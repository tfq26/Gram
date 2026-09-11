using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class NonInteractiveProcessTests
{
    [Theory]
    [InlineData("npm init", "npm init -y")]
    [InlineData("npm init -y", "npm init -y")]
    [InlineData("npm init --yes", "npm init --yes")]
    [InlineData("npx create-react-app my-app", "npx --yes create-react-app my-app")]
    [InlineData("npx create-next-app@latest", "npx --yes create-next-app@latest")]
    [InlineData("npx degit some/repo", "npx --yes degit some/repo")]
    [InlineData("dotnet new console", "dotnet new console")]
    [InlineData("cargo new my_project", "cargo new my_project")]
    public void NormalizeNonInteractiveCommand_InjectsNonInteractiveFlags(string input, string expected)
    {
        var normalized = NativeProcessRunner.NormalizeNonInteractiveCommand(input);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public async Task NativeProcessRunner_ExecutesNonInteractively_WithoutFreezing()
    {
        var runner = new NativeProcessRunner();
        var tempDir = Path.GetTempPath();

        // Run a command that outputs text
        var result = await runner.ExecuteAsync(new ProcessExecutionOptions
        {
            CommandLine = OperatingSystem.IsWindows() ? "echo HelloNonInteractive" : "echo 'HelloNonInteractive'",
            WorkingDirectory = tempDir,
            WorkspaceRoot = tempDir,
            TimeoutSeconds = 5
        });

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("HelloNonInteractive", result.CombinedOutput);
    }
}
