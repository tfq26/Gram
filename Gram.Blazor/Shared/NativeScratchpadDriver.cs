using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public interface IGramBoxDriver
{
    GramBoxEngineType EngineType { get; }
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<GramBoxExecutionResult> ExecuteCommandAsync(GramBoxSandboxSession session, string command, string? relativeSubDir = null, int timeoutSeconds = 120, CancellationToken ct = default);
    Task<bool> WriteFileAsync(GramBoxSandboxSession session, string relativePath, string content, CancellationToken ct = default);
    Task<string?> ReadFileAsync(GramBoxSandboxSession session, string relativePath, CancellationToken ct = default);
}

public sealed class NativeScratchpadDriver : IGramBoxDriver
{
    private readonly ILogger<NativeScratchpadDriver> _logger;
    private readonly GramBoxConfig _config;
    private readonly string _sharedCacheRoot;

    public GramBoxEngineType EngineType => GramBoxEngineType.NativeWorktree;

    private static readonly HashSet<string> SensitiveEnvVars = new(StringComparer.OrdinalIgnoreCase)
    {
        "OPENAI_API_KEY",
        "ANTHROPIC_API_KEY",
        "GEMINI_API_KEY",
        "OPENROUTER_API_KEY",
        "AWS_ACCESS_KEY_ID",
        "AWS_SECRET_ACCESS_KEY",
        "GITHUB_TOKEN",
        "GITLAB_TOKEN",
        "AZURE_OPENAI_API_KEY"
    };

    public NativeScratchpadDriver(ILogger<NativeScratchpadDriver> logger, GramBoxConfig? config = null)
    {
        _logger = logger;
        _config = config ?? new GramBoxConfig();

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _sharedCacheRoot = Path.Combine(homeDir, ".gram", "box", "cache");
        try { Directory.CreateDirectory(_sharedCacheRoot); } catch {}
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        // Native process execution is 100% available out of the box on Windows, macOS, and Linux
        return Task.FromResult(true);
    }

    public async Task<GramBoxExecutionResult> ExecuteCommandAsync(
        GramBoxSandboxSession session, 
        string command, 
        string? relativeSubDir = null, 
        int timeoutSeconds = 120, 
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var workingDir = string.IsNullOrWhiteSpace(relativeSubDir)
            ? session.WorktreePath
            : Path.GetFullPath(Path.Combine(session.WorktreePath, relativeSubDir));

        if (!Directory.Exists(workingDir))
        {
            Directory.CreateDirectory(workingDir);
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var shell = isWindows ? "cmd.exe" : "/bin/bash";
        var args = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"";

        // Prepend nice +10 on macOS/Linux for low CPU priority
        if (!isWindows && _config.EnableLowCpuPriority)
        {
            args = $"-c \"nice -n 10 {command.Replace("\"", "\\\"")}\"";
        }

        var psi = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // 1. Environment Sanitization
        if (_config.StripSensitiveEnvVars)
        {
            foreach (var secretKey in SensitiveEnvVars)
            {
                psi.EnvironmentVariables.Remove(secretKey);
            }
        }

        // 2. Shared Global Dependency Cache Injection (0 MB duplicate dependencies)
        if (_config.SharedDependencyCaching)
        {
            psi.EnvironmentVariables["NUGET_PACKAGES"] = Path.Combine(_sharedCacheRoot, "nuget");
            psi.EnvironmentVariables["UV_CACHE_DIR"] = Path.Combine(_sharedCacheRoot, "uv");
            psi.EnvironmentVariables["PNPM_HOME"] = Path.Combine(_sharedCacheRoot, "pnpm");
            psi.EnvironmentVariables["DOTNET_CLI_HOME"] = Path.Combine(_sharedCacheRoot, "dotnet");
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        try
        {
            session.Status = SandboxStatus.RunningCommand;
            session.CurrentCommand = command;
            session.LastActiveAt = DateTime.UtcNow;

            process.Start();

            // Set background process priority class on Windows
            if (isWindows && _config.EnableLowCpuPriority)
            {
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch {}
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            await process.WaitForExitAsync(linkedCts.Token);
            sw.Stop();

            session.Status = SandboxStatus.Ready;
            session.CurrentCommand = null;

            return new GramBoxExecutionResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Stdout = stdout.ToString().TrimEnd(),
                Stderr = stderr.ToString().TrimEnd(),
                ElapsedMs = sw.ElapsedMilliseconds,
                SandboxId = session.Id
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            try { process.Kill(entireProcessTree: true); } catch {}

            session.Status = SandboxStatus.Error;
            session.LastError = $"Execution timed out after {timeoutSeconds}s or was cancelled.";

            return new GramBoxExecutionResult
            {
                Success = false,
                ExitCode = -1,
                Stdout = stdout.ToString().TrimEnd(),
                Stderr = "Process execution timed out or was cancelled by user.",
                ErrorMessage = session.LastError,
                ElapsedMs = sw.ElapsedMilliseconds,
                SandboxId = session.Id
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            session.Status = SandboxStatus.Error;
            session.LastError = ex.Message;

            return new GramBoxExecutionResult
            {
                Success = false,
                ExitCode = -1,
                Stdout = stdout.ToString().TrimEnd(),
                Stderr = ex.Message,
                ErrorMessage = ex.Message,
                ElapsedMs = sw.ElapsedMilliseconds,
                SandboxId = session.Id
            };
        }
    }

    public async Task<bool> WriteFileAsync(GramBoxSandboxSession session, string relativePath, string content, CancellationToken ct = default)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(session.WorktreePath, relativePath));
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(fullPath, content, ct);
            session.LastActiveAt = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write file {Path} in sandbox {Id}", relativePath, session.Id);
            return false;
        }
    }

    public async Task<string?> ReadFileAsync(GramBoxSandboxSession session, string relativePath, CancellationToken ct = default)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(session.WorktreePath, relativePath));
            if (!File.Exists(fullPath)) return null;

            session.LastActiveAt = DateTime.UtcNow;
            return await File.ReadAllTextAsync(fullPath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file {Path} from sandbox {Id}", relativePath, session.Id);
            return null;
        }
    }
}
