using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Gram.Blazor.Shared;
using Porta.Pty;

namespace Gram.Desktop.Services;

public sealed class PtySession : IAsyncDisposable
{
    public string TabId { get; }
    public IPtyConnection Connection { get; }
    public CancellationTokenSource Cts { get; }
    public Task ReadLoopTask { get; set; } = Task.CompletedTask;
    public string ShellPath { get; }
    public string WorkspacePath { get; }
    public int Cols { get; set; }
    public int Rows { get; set; }
    public int Pid => Connection.Pid;
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    private readonly StringBuilder _history = new();
    private readonly object _historyLock = new();
    private const int MaxHistoryChars = 2_000_000;
    private const int TrimTargetChars = 1_500_000;

    public void AppendOutput(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_historyLock)
        {
            _history.Append(text);
            if (_history.Length > MaxHistoryChars)
            {
                var removeCount = _history.Length - TrimTargetChars;
                _history.Remove(0, removeCount);
            }
        }
    }

    public string GetHistory()
    {
        lock (_historyLock)
        {
            return _history.ToString();
        }
    }

    public void ClearHistory()
    {
        lock (_historyLock)
        {
            _history.Clear();
        }
    }

    public PtySession(string tabId, IPtyConnection connection, CancellationTokenSource cts, string shellPath, string workspacePath, int cols, int rows)
    {
        TabId = tabId;
        Connection = connection;
        Cts = cts;
        ShellPath = shellPath;
        WorkspacePath = workspacePath;
        Cols = cols;
        Rows = rows;
    }

    public async ValueTask DisposeAsync()
    {
        Cts.Cancel();
        try
        {
            Connection.Kill();
        }
        catch { }
        Connection.Dispose();
        Cts.Dispose();
        ClearHistory();
        await Task.CompletedTask;
    }
}

public sealed class PtyTerminalService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, PtySession> _sessions = new();
    private readonly WorkspaceTabService? _tabService;

    public event Action<string, string>? OnOutputReceived;
    public event Action<string, int>? OnProcessExited;

    public PtyTerminalService(WorkspaceTabService? tabService = null)
    {
        _tabService = tabService;
        if (_tabService != null)
        {
            _tabService.OnTabClosed += (tabId) => _ = CloseSessionAsync(tabId);
        }
    }

    public async Task<PtySession> StartOrGetSessionAsync(string tabId, string? workspacePath, int cols = 80, int rows = 24, string? shellOverride = null)
    {
        if (_sessions.TryGetValue(tabId, out var existing))
        {
            return existing;
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var shell = shellOverride;
        if (string.IsNullOrWhiteSpace(shell))
        {
            if (isWindows)
            {
                var pwsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
                shell = File.Exists(pwsh) ? pwsh : Path.Combine(Environment.SystemDirectory, "cmd.exe");
            }
            else
            {
                var userShell = Environment.GetEnvironmentVariable("SHELL");
                if (!string.IsNullOrWhiteSpace(userShell) && File.Exists(userShell))
                {
                    shell = userShell;
                }
                else if (File.Exists("/bin/zsh"))
                {
                    shell = "/bin/zsh";
                }
                else
                {
                    shell = "/bin/bash";
                }
            }
        }

        var cwd = !string.IsNullOrWhiteSpace(workspacePath) && Directory.Exists(workspacePath)
            ? workspacePath
            : Environment.CurrentDirectory;

        var options = new PtyOptions
        {
            Name = $"GramTerminal-{tabId}",
            Cols = Math.Max(cols, 20),
            Rows = Math.Max(rows, 5),
            Cwd = cwd,
            App = shell,
            Environment = new Dictionary<string, string>
            {
                { "TERM", "xterm-256color" },
                { "COLORTERM", "truecolor" },
                { "LANG", "en_US.UTF-8" },
                { "LC_ALL", "en_US.UTF-8" }
            }
        };

        var ptyConnection = await PtyProvider.SpawnAsync(options, CancellationToken.None);
        var cts = new CancellationTokenSource();
        var session = new PtySession(tabId, ptyConnection, cts, shell, cwd, cols, rows);

        ptyConnection.ProcessExited += (s, e) =>
        {
            OnProcessExited?.Invoke(tabId, e.ExitCode);
            _sessions.TryRemove(tabId, out _);
        };

        // Start background reading loop
        session.ReadLoopTask = Task.Run(async () =>
        {
            var buffer = new byte[4096];
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var read = await ptyConnection.ReaderStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (read <= 0) break;
                    var text = Encoding.UTF8.GetString(buffer, 0, read);
                    session.AppendOutput(text);
                    OnOutputReceived?.Invoke(tabId, text);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Gram.Blazor.Shared.GramLog.Error("PtyTerminalService", $"Error in read loop for {tabId}: {ex.Message}");
            }
        }, cts.Token);

        _sessions[tabId] = session;
        return session;
    }

    public string GetHistory(string tabId)
    {
        if (_sessions.TryGetValue(tabId, out var session))
        {
            return session.GetHistory();
        }
        return string.Empty;
    }

    public void ClearHistory(string tabId)
    {
        if (_sessions.TryGetValue(tabId, out var session))
        {
            session.ClearHistory();
        }
    }

    public async Task WriteInputAsync(string tabId, string data)
    {
        if (_sessions.TryGetValue(tabId, out var session))
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(data);
                await session.Connection.WriterStream.WriteAsync(bytes, 0, bytes.Length);
                await session.Connection.WriterStream.FlushAsync();
            }
            catch (Exception ex)
            {
                Gram.Blazor.Shared.GramLog.Error("PtyTerminalService", $"Failed to write to terminal {tabId}: {ex.Message}");
            }
        }
    }

    public void Resize(string tabId, int cols, int rows)
    {
        if (_sessions.TryGetValue(tabId, out var session))
        {
            try
            {
                session.Cols = cols;
                session.Rows = rows;
                session.Connection.Resize(Math.Max(cols, 10), Math.Max(rows, 5));
            }
            catch { }
        }
    }

    public PtySession? GetSession(string tabId)
    {
        _sessions.TryGetValue(tabId, out var session);
        return session;
    }

    public async Task CloseSessionAsync(string tabId)
    {
        if (_sessions.TryRemove(tabId, out var session))
        {
            await session.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _sessions)
        {
            await kvp.Value.DisposeAsync();
        }
        _sessions.Clear();
    }
}
