using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Gram.Blazor.Shared;
using Gram.Desktop;
using Gram.LocalAgent;
using GramBox.Contracts;
using Microsoft.AspNetCore.Builder;
using Xunit;
using ApiError = GramBox.Contracts.ApiError;

namespace Gram.Blazor.UnitTests;

public sealed class LocalAgentProcessTests
{
    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public void LocalPathGuard_StrictlyRestrictsToWorkspaceRoot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gram-guard-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var subDir = Path.Combine(tempDir, "sub");
            Directory.CreateDirectory(subDir);

            // Valid subpaths
            Assert.True(LocalPathGuard.IsWithinRoot(tempDir, "file.txt"));
            Assert.True(LocalPathGuard.IsWithinRoot(tempDir, "sub/file.txt"));
            Assert.True(LocalPathGuard.IsWithinRoot(tempDir, "sub\\file.txt"));
            Assert.True(LocalPathGuard.IsWithinRoot(tempDir, "."));

            // Path traversal attacks
            Assert.False(LocalPathGuard.IsWithinRoot(tempDir, "../outside.txt"));
            Assert.False(LocalPathGuard.IsWithinRoot(tempDir, "..\\outside.txt"));
            Assert.False(LocalPathGuard.IsWithinRoot(tempDir, "sub/../../escape.txt"));
            Assert.False(LocalPathGuard.IsWithinRoot(tempDir, "sub\\..\\..\\escape.txt"));
            Assert.False(LocalPathGuard.IsWithinRoot(tempDir, "C:\\Windows\\System32\\cmd.exe"));
            Assert.False(LocalPathGuard.IsWithinRoot(tempDir, "/etc/passwd"));
            Assert.False(LocalPathGuard.IsWithinRoot(tempDir, ""));
            Assert.False(LocalPathGuard.IsWithinRoot(tempDir, "   "));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task LocalAgent_RejectsMissingOrInvalidSecret()
    {
        var port = GetFreePort();
        var secret = "test-secret-" + Guid.NewGuid().ToString("N");
        var app = Program.CreateApp([], explicitSecret: secret, explicitPort: port);
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            // Health is unauthenticated
            var healthResp = await client.GetAsync("/local/v1/health");
            Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);

            // No secret header -> 401 Unauthorized
            var unauthResp = await client.GetAsync("/local/v1/capabilities");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthResp.StatusCode);

            // Wrong secret header -> 401 Unauthorized
            using var wrongReq = new HttpRequestMessage(HttpMethod.Get, "/local/v1/capabilities");
            wrongReq.Headers.Add("X-Local-Agent-Secret", "wrong-secret");
            var wrongResp = await client.SendAsync(wrongReq);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResp.StatusCode);

            // Correct secret header -> 200 OK
            using var validReq = new HttpRequestMessage(HttpMethod.Get, "/local/v1/capabilities");
            validReq.Headers.Add("X-Local-Agent-Secret", secret);
            var validResp = await client.SendAsync(validReq);
            Assert.Equal(HttpStatusCode.OK, validResp.StatusCode);

            // Correct secret via Authorization: Bearer -> 200 OK
            using var bearerReq = new HttpRequestMessage(HttpMethod.Get, "/local/v1/capabilities");
            bearerReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
            var bearerResp = await client.SendAsync(bearerReq);
            Assert.Equal(HttpStatusCode.OK, bearerResp.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task LocalAgent_RejectsCrossClientOrNonLocalHostHeader()
    {
        var port = GetFreePort();
        var secret = "test-secret-" + Guid.NewGuid().ToString("N");
        var app = Program.CreateApp([], explicitSecret: secret, explicitPort: port);
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            using var hostileReq = new HttpRequestMessage(HttpMethod.Get, "/local/v1/health");
            hostileReq.Headers.Add("Host", "attacker.com:7340");
            var hostileResp = await client.SendAsync(hostileReq);
            Assert.Equal(HttpStatusCode.Forbidden, hostileResp.StatusCode);

            using var validReq = new HttpRequestMessage(HttpMethod.Get, "/local/v1/health");
            validReq.Headers.Add("Host", $"127.0.0.1:{port}");
            var validResp = await client.SendAsync(validReq);
            Assert.Equal(HttpStatusCode.OK, validResp.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task LocalAgent_FilesPick_BlocksTraversalAndReadsAllowedFiles()
    {
        var port = GetFreePort();
        var secret = "test-secret-" + Guid.NewGuid().ToString("N");
        var app = Program.CreateApp([], explicitSecret: secret, explicitPort: port);
        await app.StartAsync();

        var workspace = Path.Combine(Path.GetTempPath(), "gram-files-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var testFile = Path.Combine(workspace, "allowed.txt");
        File.WriteAllText(testFile, "secure content");

        try
        {
            var agent = new DesktopLocalAgent(new DesktopLocalAgentOptions(
                Endpoint: $"http://127.0.0.1:{port}",
                Secret: secret
            ));

            // Read allowed file
            var pickResult = await agent.PickFileAsync(new FilePickRequest(WorkspaceRoot: workspace, RelativePath: "allowed.txt"));
            Assert.True(pickResult.Success);
            Assert.Equal("secure content", pickResult.Content);

            // Reject traversal
            var traversalResult = await agent.PickFileAsync(new FilePickRequest(WorkspaceRoot: workspace, RelativePath: "../../boot.ini"));
            Assert.False(traversalResult.Success);

            // Non-existent file
            var missingResult = await agent.PickFileAsync(new FilePickRequest(WorkspaceRoot: workspace, RelativePath: "missing.txt"));
            Assert.False(missingResult.Success);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(workspace, true); } catch { }
        }
    }

    [Fact]
    public async Task LocalAgent_TerminalRun_ExecutesAndEnforcesTimeoutCancellation()
    {
        var port = GetFreePort();
        var secret = "test-secret-" + Guid.NewGuid().ToString("N");
        var app = Program.CreateApp([], explicitSecret: secret, explicitPort: port);
        await app.StartAsync();

        try
        {
            var agent = new DesktopLocalAgent(new DesktopLocalAgentOptions(
                Endpoint: $"http://127.0.0.1:{port}",
                Secret: secret
            ));

            // Execute normal command
            var cmd = OperatingSystem.IsWindows() ? "echo GramLocalAgentRunning" : "echo GramLocalAgentRunning";
            var runResult = await agent.RunTerminalAsync(new TerminalRunRequest(cmd, TimeoutSeconds: 10));
            Assert.True(runResult.Success, $"Failed: {runResult.Error}, stderr: {runResult.Stderr}, stdout: {runResult.Stdout}, code: {runResult.ExitCode}");
            Assert.Equal(0, runResult.ExitCode);
            Assert.Contains("GramLocalAgentRunning", runResult.Stdout);

            // Execute command with short timeout to test process cancellation / termination
            var sleepCmd = OperatingSystem.IsWindows() ? "powershell -Command \"Start-Sleep -Seconds 15\"" : "sleep 15";
            var timeoutResult = await agent.RunTerminalAsync(new TerminalRunRequest(sleepCmd, TimeoutSeconds: 1));
            Assert.False(timeoutResult.Success);
            Assert.Equal(-1, timeoutResult.ExitCode);
            Assert.Contains("TIMED_OUT", timeoutResult.Error ?? "");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task LocalAgent_SandboxCreate_CreatesDirectoryAndReturnsId()
    {
        var port = GetFreePort();
        var secret = "test-secret-" + Guid.NewGuid().ToString("N");
        var app = Program.CreateApp([], explicitSecret: secret, explicitPort: port);
        await app.StartAsync();

        var baseDir = Path.Combine(Path.GetTempPath(), "gram-sandbox-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);

        try
        {
            var agent = new DesktopLocalAgent(new DesktopLocalAgentOptions(
                Endpoint: $"http://127.0.0.1:{port}",
                Secret: secret
            ));

            var result = await agent.CreateSandboxAsync(new LocalSandboxCreateRequest(Name: "test-box", RunId: "run-1", BaseDirectory: baseDir));
            Assert.True(result.Success);
            Assert.StartsWith("sbx-", result.SandboxId);
            Assert.True(Directory.Exists(result.SandboxPath));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    [Fact]
    public async Task DesktopSession_WhenLocalAgentConnected_DispatchesCapabilities()
    {
        var port = GetFreePort();
        var secret = "test-secret-" + Guid.NewGuid().ToString("N");
        var app = Program.CreateApp([], explicitSecret: secret, explicitPort: port);
        await app.StartAsync();

        var tempDir = Path.Combine(Path.GetTempPath(), "gram-session-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "sample.txt"), "hello from local workspace");

        try
        {
            var agent = new DesktopLocalAgent(new DesktopLocalAgentOptions(
                Endpoint: $"http://127.0.0.1:{port}",
                Secret: secret
            ));

            var fakeBackend = new GramBackendClient(new HttpClient { BaseAddress = new Uri("http://localhost:7337") });
            var session = new DesktopSession(fakeBackend, backendUrl: "http://localhost:7337", localAgent: agent);
            session.SetWorkspace(tempDir);

            // Initially before refresh, status is offline
            Assert.False(session.IsLocalAgentConnected);

            // After refresh, local agent connects
            await session.RefreshLocalAgentAsync();
            Assert.True(session.IsLocalAgentConnected);
            Assert.False(session.IsLocalAgentUnauthorized);
            Assert.Contains(session.LocalAgentCapabilities, c => c.Name == "terminal.run");

            // Pick workspace file
            var pickRes = await session.PickWorkspaceFileAsync("sample.txt");
            Assert.True(pickRes.Success);
            Assert.Equal("hello from local workspace", pickRes.Content);

            // Run terminal command
            var cmd = OperatingSystem.IsWindows() ? "cmd /c echo session-ok" : "echo session-ok";
            var termRes = await session.RunTerminalCommandAsync(cmd);
            Assert.True(termRes.Success);
            Assert.Equal(0, termRes.ExitCode);
            Assert.Contains("session-ok", termRes.Stdout);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
