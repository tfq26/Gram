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
    private readonly IPlatformSandboxController? _platformController;
    private readonly ISandboxScheduler? _scheduler;

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

    public NativeScratchpadDriver(ILogger<NativeScratchpadDriver> logger, GramBoxConfig? config = null, IPlatformSandboxController? platformController = null, ISandboxScheduler? scheduler = null)
    {
        _logger = logger;
        _config = config ?? new GramBoxConfig();
        _platformController = platformController;
        _scheduler = scheduler;

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
        timeoutSeconds = Math.Clamp(timeoutSeconds, 1, _config.MaxProcessTimeoutSeconds);
        if (!SandboxNetworkPolicy.CanExecuteCommand(command, session.NetworkMode, session.AllowedNetworkHosts, out var networkError))
        {
            return new GramBoxExecutionResult { Success = false, ExitCode = -1, ErrorMessage = networkError, Stderr = networkError ?? "Network policy blocked the command.", SandboxId = session.Id };
        }
        if (!SandboxPathGuard.TryResolve(session.WorktreePath, relativeSubDir, out var workingDir, out var pathError))
        {
            return new GramBoxExecutionResult { Success = false, ExitCode = -1, ErrorMessage = pathError, Stderr = pathError ?? "Invalid sandbox path.", SandboxId = session.Id };
        }

        if (!Directory.Exists(workingDir))
        {
            Directory.CreateDirectory(workingDir);
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var shell = isWindows ? "cmd.exe" : "/bin/bash";
        var effectiveCommand = command;
        if (!isWindows && _config.EnableUnixResourceLimits)
        {
            var budget = session.ResourceBudget;
            var memoryMb = Math.Max(128, (int)Math.Min(int.MaxValue / 1024, (budget?.MemoryBytes ?? _config.MaxMemoryQuotaMb * 1024L * 1024L) / (1024L * 1024L)));
            var cpuSeconds = Math.Max(1, timeoutSeconds * Math.Max(1, budget?.CpuCores ?? 1));
            var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            effectiveCommand = isMac
                ? command
                : $"ulimit -t {cpuSeconds} 2>/dev/null || true; ulimit -v {memoryMb * 1024L} 2>/dev/null || true; ulimit -u {_config.MaxChildProcesses} 2>/dev/null || true; {command}";
        }
        var args = isWindows ? $"/c {effectiveCommand}" : $"-c \"{effectiveCommand.Replace("\"", "\\\"")}\"";

        // Prepend nice +10 on macOS/Linux for low CPU priority
        if (!isWindows && _config.EnableLowCpuPriority && !_config.EnableUnixResourceLimits)
        {
            args = $"-c \"nice -n 10 {effectiveCommand.Replace("\"", "\\\"")}\"";
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
            foreach (var secretKey in SensitiveEnvVars.Concat(Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(SandboxNetworkPolicy.IsSensitiveEnvironmentVariable)))
            {
                psi.EnvironmentVariables.Remove(secretKey);
            }
        }
        psi.EnvironmentVariables["GRAM_SANDBOX_NETWORK_MODE"] = session.NetworkMode.ToString();
        psi.EnvironmentVariables["GRAM_SANDBOX_ALLOWED_HOSTS"] = string.Join(",", session.AllowedNetworkHosts);

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
        var outputBytes = 0;
        var outputLimitExceeded = false;
        var resourceLimitExceeded = false;
        string? resourceLimitError = null;
        long peakWorkingSetBytes = 0;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            var totalBytes = Interlocked.Add(ref outputBytes, Encoding.UTF8.GetByteCount(e.Data) + Environment.NewLine.Length);
            if (totalBytes <= _config.MaxOutputBytes) stdout.AppendLine(e.Data);
            else outputLimitExceeded = true;
            if (outputLimitExceeded) { try { process.Kill(entireProcessTree: true); } catch {} }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            var totalBytes = Interlocked.Add(ref outputBytes, Encoding.UTF8.GetByteCount(e.Data) + Environment.NewLine.Length);
            if (totalBytes <= _config.MaxOutputBytes) stderr.AppendLine(e.Data);
            else outputLimitExceeded = true;
            if (outputLimitExceeded) { try { process.Kill(entireProcessTree: true); } catch {} }
        };

        try
        {
            session.Status = SandboxStatus.RunningCommand;
            session.CurrentCommand = command;
            session.LastActiveAt = DateTime.UtcNow;

            using var executionReservation = _scheduler is null || session.ResourceBudget is null
                ? null
                : await _scheduler.AcquireExecutionAsync(session.ResourceBudget, ct);
            process.Start();
            using var platformAttachment = _platformController is null || session.ResourceBudget is null
                ? null
                : await _platformController.AttachAsync(process, session.ResourceBudget, session.Id, ct);

            // Set background process priority class on Windows
            if (isWindows && _config.EnableLowCpuPriority)
            {
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch {}
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCts.Token);
            var monitorTask = MonitorProcessAsync(process, session, timeoutSeconds, workingSet =>
            {
                InterlockedMax(ref peakWorkingSetBytes, workingSet);
            }, () =>
            {
                resourceLimitExceeded = true;
                resourceLimitError = "Sandbox process exceeded its assigned resource budget.";
            }, monitorCts.Token);

            await process.WaitForExitAsync(linkedCts.Token);
            monitorCts.Cancel();
            try { await monitorTask; } catch (OperationCanceledException) { }
            sw.Stop();

            session.Status = SandboxStatus.Ready;
            session.CurrentCommand = null;

            long cpuTimeMs = 0;
            try
            {
                cpuTimeMs = (long)process.TotalProcessorTime.TotalMilliseconds;
            }
            catch { }

            return new GramBoxExecutionResult
            {
                Success = process.ExitCode == 0 && !outputLimitExceeded && !resourceLimitExceeded,
                ExitCode = process.ExitCode,
                Stdout = stdout.ToString().TrimEnd(),
                Stderr = stderr.ToString().TrimEnd(),
                ErrorMessage = outputLimitExceeded ? $"Process output exceeded the sandbox limit of {_config.MaxOutputBytes} bytes." : resourceLimitError,
                ElapsedMs = sw.ElapsedMilliseconds,
                PeakWorkingSetBytes = peakWorkingSetBytes,
                CpuTimeMs = cpuTimeMs,
                OutputBytes = outputBytes,
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

    private async Task MonitorProcessAsync(Process process, GramBoxSandboxSession session, int timeoutSeconds, Action<long> onSample, Action onLimit, CancellationToken cancellationToken)
    {
        var budget = session.ResourceBudget;
        var memoryLimit = budget?.MemoryBytes ?? _config.MaxMemoryQuotaMb * 1024L * 1024L;
        var cpuLimit = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds * Math.Max(1, budget?.CpuCores ?? 1)));
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(250, cancellationToken);
            if (process.HasExited) return;
            try
            {
                onSample(process.WorkingSet64);
                if (memoryLimit > 0 && process.WorkingSet64 > memoryLimit)
                {
                    onLimit();
                    process.Kill(entireProcessTree: true);
                    return;
                }
                if (process.TotalProcessorTime > cpuLimit)
                {
                    onLimit();
                    process.Kill(entireProcessTree: true);
                    return;
                }
            }
            catch (InvalidOperationException) { return; }
            catch (System.ComponentModel.Win32Exception) { return; }
        }
    }

    private static void InterlockedMax(ref long location, long value)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref location);
            if (value <= current) return;
        } while (Interlocked.CompareExchange(ref location, value, current) != current);
    }

    private static int TryGetExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return -1; }
    }

    private static long TryGetCpuTimeMs(Process process)
    {
        try { return (long)process.TotalProcessorTime.TotalMilliseconds; } catch { return 0; }
    }

    public async Task<bool> WriteFileAsync(GramBoxSandboxSession session, string relativePath, string content, CancellationToken ct = default)
    {
        try
        {
            if (!SandboxPathGuard.TryResolve(session.WorktreePath, relativePath, out var fullPath, out var pathError))
            {
                _logger.LogWarning("Blocked write outside sandbox {Id}: {Path} ({Reason})", session.Id, relativePath, pathError);
                return false;
            }
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
            if (!SandboxPathGuard.TryResolve(session.WorktreePath, relativePath, out var fullPath, out var pathError))
            {
                _logger.LogWarning("Blocked read outside sandbox {Id}: {Path} ({Reason})", session.Id, relativePath, pathError);
                return null;
            }
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
