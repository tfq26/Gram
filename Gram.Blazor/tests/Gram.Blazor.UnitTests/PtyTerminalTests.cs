using System.Runtime.InteropServices;
using System.Text;
using Porta.Pty;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class PtyTerminalTests
{
    [Fact]
    public async Task PtyProvider_CanSpawnShellAndReadOutput()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var shell = isWindows
            ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
            : (File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash");

        var options = new PtyOptions
        {
            Name = "TestTerminal",
            Cols = 80,
            Rows = 24,
            Cwd = Environment.CurrentDirectory,
            App = shell,
            Environment = new Dictionary<string, string>
            {
                { "TERM", "xterm-256color" }
            }
        };

        using var terminal = await PtyProvider.SpawnAsync(options, CancellationToken.None);
        Assert.NotNull(terminal);
        Assert.True(terminal.Pid > 0);

        // Write a simple echo command
        var cmdBytes = Encoding.UTF8.GetBytes("echo PTY_TEST_OK\r\n");
        await terminal.WriterStream.WriteAsync(cmdBytes, 0, cmdBytes.Length);
        await terminal.WriterStream.FlushAsync();

        // Read output until we see PTY_TEST_OK
        var buffer = new byte[2048];
        var sb = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!cts.IsCancellationRequested && !sb.ToString().Contains("PTY_TEST_OK"))
        {
            var read = await terminal.ReaderStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            if (read <= 0) break;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }

        Assert.Contains("PTY_TEST_OK", sb.ToString());
        terminal.Kill();
    }

    [Fact]
    public void PtySession_BuffersHistoryAndClearsCorrectly()
    {
        var dummyConnection = new MoqDummyConnection();
        using var cts = new CancellationTokenSource();
        var session = new Gram.Desktop.Services.PtySession(
            "tab-test",
            dummyConnection,
            cts,
            "/bin/zsh",
            Environment.CurrentDirectory,
            80,
            24
        );

        Assert.Equal(string.Empty, session.GetHistory());

        session.AppendOutput("echo hello\r\n");
        session.AppendOutput("hello\r\n");

        Assert.Equal("echo hello\r\nhello\r\n", session.GetHistory());

        session.ClearHistory();
        Assert.Equal(string.Empty, session.GetHistory());
    }

    [Fact]
    public async Task PtyTerminalService_PreservesHistoryAcrossQueries()
    {
        var service = new Gram.Desktop.Services.PtyTerminalService();
        var tabId = "tab-persistent-" + Guid.NewGuid().ToString("N");

        var session = await service.StartOrGetSessionAsync(tabId, Environment.CurrentDirectory);
        Assert.NotNull(session);

        session.AppendOutput("sample command output\r\n");
        var history = service.GetHistory(tabId);
        Assert.Contains("sample command output", history);

        // Fetching existing session should return the same instance and retain history
        var sameSession = await service.StartOrGetSessionAsync(tabId, Environment.CurrentDirectory);
        Assert.Same(session, sameSession);
        Assert.Contains("sample command output", service.GetHistory(tabId));

        await service.CloseSessionAsync(tabId);
        Assert.Equal(string.Empty, service.GetHistory(tabId));
    }

    private sealed class MoqDummyConnection : IPtyConnection
    {
        public int Pid => 12345;
        public int ExitCode => 0;
        public Stream ReaderStream => Stream.Null;
        public Stream WriterStream => Stream.Null;
        public event EventHandler<PtyExitedEventArgs>? ProcessExited { add { } remove { } }

        public void Kill() { }
        public void Resize(int cols, int rows) { }
        public bool WaitForExit(int millisecondsTimeout) => true;
        public void Dispose() { }
    }
}
