using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class ProcessExecutionOptions
{
    public required string CommandLine { get; set; }
    public required string WorkingDirectory { get; set; }
    public required string WorkspaceRoot { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxBufferSizeBytes { get; set; } = 64 * 1024; // 64KB
    public bool ScrubEnvironment { get; set; } = true;
    public bool EnforceProcessTreeKill { get; set; } = true;
    public string? RunId { get; set; }
    public Action<string>? OutputLineHandler { get; set; }
}

public sealed class ProcessExecutionResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public bool TimedOut { get; set; }
    public bool WasTruncated { get; set; }
    public bool Cancelled { get; set; }

    public string CombinedOutput =>
        string.IsNullOrWhiteSpace(StandardOutput)
            ? StandardError
            : (string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : $"{StandardOutput}\n{StandardError}");
}

public sealed record ProcessAuditEntry(
    DateTimeOffset Timestamp,
    string CommandLine,
    string WorkingDirectory,
    string WorkspaceRoot,
    int ExitCode,
    long DurationMs,
    bool TimedOut,
    bool Truncated,
    string? RunId = null);

public interface INativeProcessRunner
{
    Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionOptions options, CancellationToken ct = default);
}

public sealed class NativeProcessRunner : INativeProcessRunner
{
    private static readonly object AuditFileLock = new();

    public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CommandLine);

        var sw = Stopwatch.StartNew();
        var cmd = NormalizeNonInteractiveCommand(options.CommandLine);
        var workDir = Directory.Exists(options.WorkingDirectory)
            ? options.WorkingDirectory
            : (Directory.Exists(options.WorkspaceRoot) ? options.WorkspaceRoot : Directory.GetCurrentDirectory());

        var (shell, shellArgs) = ResolveShellAndArgs(cmd);

        var psi = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = shellArgs,
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Ensure automated executions are non-interactive to prevent input freezing
        psi.Environment["CI"] = "true";
        psi.Environment["DEBIAN_FRONTEND"] = "noninteractive";
        psi.Environment["NONINTERACTIVE"] = "1";
        psi.Environment["npm_config_yes"] = "true";

        if (options.ScrubEnvironment)
        {
            EnvironmentScrubber.Scrub(psi);
        }

        using var proc = new Process { StartInfo = psi };
        var stdoutCollector = new BoundedOutputCollector(options.MaxBufferSizeBytes);
        var stderrCollector = new BoundedOutputCollector(options.MaxBufferSizeBytes);

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdoutCollector.AppendLine(e.Data);
                try { options.OutputLineHandler?.Invoke(e.Data); } catch { }
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderrCollector.AppendLine(e.Data);
                try { options.OutputLineHandler?.Invoke(e.Data); } catch { }
            }
        };

        var result = new ProcessExecutionResult();

        var timeoutSecs = Math.Max(options.TimeoutSeconds, 5);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSecs));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            proc.Start();
            proc.StandardInput.Close();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(linkedCts.Token);
            result.ExitCode = proc.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (options.EnforceProcessTreeKill)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch { }
            }

            if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                result.TimedOut = true;
                result.ExitCode = -1;
            }
            else
            {
                result.Cancelled = true;
                result.ExitCode = -2;
            }
        }
        catch (Exception ex)
        {
            stderrCollector.AppendLine($"Failed to execute process: {ex.Message}");
            result.ExitCode = -3;
        }
        finally
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;

            var (stdoutText, stdoutTrunc) = stdoutCollector.GetOutput();
            var (stderrText, stderrTrunc) = stderrCollector.GetOutput();

            result.StandardOutput = stdoutText;
            result.StandardError = stderrText;
            result.WasTruncated = stdoutTrunc || stderrTrunc;

            if (result.TimedOut)
            {
                result.StandardError = $"Command timed out after {timeoutSecs} seconds: {cmd}\n{result.StandardError}".Trim();
            }

            // Write audit record
            RecordAuditLog(new ProcessAuditEntry(
                Timestamp: DateTimeOffset.UtcNow,
                CommandLine: cmd,
                WorkingDirectory: workDir,
                WorkspaceRoot: options.WorkspaceRoot,
                ExitCode: result.ExitCode,
                DurationMs: result.DurationMs,
                TimedOut: result.TimedOut,
                Truncated: result.WasTruncated,
                RunId: options.RunId));
        }

        return result;
    }

    public static (string Shell, string Args) ResolveShellAndArgs(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            var escaped = command.Replace("\"", "\\\"");
            return ("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{escaped}\"");
        }
        if (OperatingSystem.IsMacOS())
        {
            var shell = File.Exists("/bin/zsh") ? "/bin/zsh" : (File.Exists("/bin/bash") ? "/bin/bash" : "sh");
            var escaped = command.Replace("\"", "\\\"");
            return (shell, $"-c \"{escaped}\"");
        }
        // Linux / Unix
        var unixShell = File.Exists("/bin/bash") ? "/bin/bash" : (File.Exists("/bin/sh") ? "/bin/sh" : "sh");
        var unixEscaped = command.Replace("\"", "\\\"");
        return (unixShell, $"-c \"{unixEscaped}\"");
    }

    public static string NormalizeNonInteractiveCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return command;
        var trimmed = command.Trim();
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^npm\s+init(?!\s+--yes|\s+-y)(\s+|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return trimmed + " -y";
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^npx\s+(?!-y|--yes)(create-[\w-]+|degit)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return System.Text.RegularExpressions.Regex.Replace(trimmed, @"^npx\s+", "npx --yes ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return trimmed;
    }

    private static void RecordAuditLog(ProcessAuditEntry entry)
    {
        try
        {
            var auditDir = Path.Combine(entry.WorkspaceRoot, ".gram", "audit_logs");
            if (!Directory.Exists(auditDir))
            {
                Directory.CreateDirectory(auditDir);
            }

            var auditFile = Path.Combine(auditDir, "process_audit.jsonl");
            var jsonLine = JsonSerializer.Serialize(entry) + Environment.NewLine;

            lock (AuditFileLock)
            {
                File.AppendAllText(auditFile, jsonLine);
            }
        }
        catch
        {
            // Logging failure should never interrupt execution
        }
    }

    private sealed class BoundedOutputCollector
    {
        private readonly int _maxBytes;
        private readonly List<string> _headLines = [];
        private readonly List<string> _tailLines = [];
        private readonly object _lock = new();
        private int _totalEstimatedBytes;
        private bool _isTruncated;
        private const int MaxTailLines = 150;

        public BoundedOutputCollector(int maxBytes)
        {
            _maxBytes = Math.Max(maxBytes, 1024);
        }

        public void AppendLine(string line)
        {
            lock (_lock)
            {
                var lineBytes = line.Length * sizeof(char);
                _totalEstimatedBytes += lineBytes;

                var headByteBudget = _maxBytes / 2;
                if (_totalEstimatedBytes <= headByteBudget && !_isTruncated)
                {
                    _headLines.Add(line);
                }
                else
                {
                    _isTruncated = true;
                    if (_tailLines.Count >= MaxTailLines)
                    {
                        _tailLines.RemoveAt(0);
                    }
                    _tailLines.Add(line);
                }
            }
        }

        public (string Output, bool IsTruncated) GetOutput()
        {
            lock (_lock)
            {
                if (!_isTruncated)
                {
                    return (string.Join(Environment.NewLine, _headLines).Trim(), false);
                }

                var sb = new StringBuilder();
                if (_headLines.Count > 0)
                {
                    sb.AppendLine(string.Join(Environment.NewLine, _headLines));
                }
                var omittedBytes = Math.Max(0, _totalEstimatedBytes - _maxBytes);
                sb.AppendLine($"\n... [output truncated: ~{omittedBytes:N0} bytes omitted] ...\n");
                if (_tailLines.Count > 0)
                {
                    sb.AppendLine(string.Join(Environment.NewLine, _tailLines));
                }

                return (sb.ToString().Trim(), true);
            }
        }
    }
}
