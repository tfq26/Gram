using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class NativeProcessRunnerTests
{
    [Fact]
    public void ShellResolution_ReturnsPlatformAppropriateShell()
    {
        var (shell, args) = NativeProcessRunner.ResolveShellAndArgs("echo test");
        Assert.False(string.IsNullOrWhiteSpace(shell));
        Assert.False(string.IsNullOrWhiteSpace(args));

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("powershell", shell, StringComparison.OrdinalIgnoreCase);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.True(shell.EndsWith("zsh") || shell.EndsWith("bash") || shell == "sh");
        }
    }

    [Fact]
    public async Task ProcessRunner_ExecutesCommandAndCapturesOutput()
    {
        var runner = new NativeProcessRunner();
        using var tempDir = new TemporaryDirectory();

        var testEcho = OperatingSystem.IsWindows() ? "Write-Output 'Hello Native Runner'" : "echo 'Hello Native Runner'";
        var result = await runner.ExecuteAsync(new ProcessExecutionOptions
        {
            CommandLine = testEcho,
            WorkingDirectory = tempDir.Path,
            WorkspaceRoot = tempDir.Path,
            TimeoutSeconds = 15
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Hello Native Runner", result.StandardOutput);
        Assert.False(result.TimedOut);
        Assert.True(result.DurationMs >= 0);
    }

    [Fact]
    public async Task ProcessRunner_RecordsAuditTrailLog()
    {
        var runner = new NativeProcessRunner();
        using var tempDir = new TemporaryDirectory();

        var testCmd = OperatingSystem.IsWindows() ? "Write-Output 'Audit Test'" : "echo 'Audit Test'";
        var result = await runner.ExecuteAsync(new ProcessExecutionOptions
        {
            CommandLine = testCmd,
            WorkingDirectory = tempDir.Path,
            WorkspaceRoot = tempDir.Path,
            TimeoutSeconds = 15,
            RunId = "run-audit-123"
        });

        var auditFile = Path.Combine(tempDir.Path, ".gram", "audit_logs", "process_audit.jsonl");
        Assert.True(File.Exists(auditFile));

        var lines = await File.ReadAllLinesAsync(auditFile);
        Assert.Contains(lines, l => l.Contains("run-audit-123") && l.Contains("Audit Test"));
    }

    [Fact]
    public async Task ProcessRunner_TruncatesExcessiveOutputWithMarker()
    {
        var runner = new NativeProcessRunner();
        using var tempDir = new TemporaryDirectory();

        // Generate output exceeding 2KB
        var genCmd = OperatingSystem.IsWindows()
            ? "1..200 | ForEach-Object { Write-Output ('Line ' + $_ + ' : ' + ('X' * 50)) }"
            : "for i in $(seq 1 200); do echo \"Line $i : XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX\"; done";

        var result = await runner.ExecuteAsync(new ProcessExecutionOptions
        {
            CommandLine = genCmd,
            WorkingDirectory = tempDir.Path,
            WorkspaceRoot = tempDir.Path,
            TimeoutSeconds = 15,
            MaxBufferSizeBytes = 2048 // 2KB small limit for test
        });

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.WasTruncated);
        Assert.Contains("[output truncated:", result.StandardOutput);
    }
}
