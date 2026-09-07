using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Gram.Blazor.Shared;
using GramBox.Contracts;
using ApiError = GramBox.Contracts.ApiError;

namespace Gram.LocalAgent;

public sealed record LocalCapabilitiesResponse(
    string Version,
    bool IsAvailable,
    IReadOnlyList<string> EnabledCapabilities,
    string? MachineName = null,
    long UptimeSeconds = 0
);

public class Program
{
    private static readonly DateTimeOffset StartTime = DateTimeOffset.UtcNow;

    public static async Task Main(string[] args)
    {
        var app = CreateApp(args);
        await app.RunAsync();
    }

    public static WebApplication CreateApp(string[] args, string? explicitSecret = null, int? explicitPort = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        var secret = ResolveOrCreateSecret(explicitSecret);

        var port = explicitPort 
            ?? (int.TryParse(Environment.GetEnvironmentVariable("PORT") ?? Environment.GetEnvironmentVariable("GRAM_LOCAL_AGENT_PORT"), out var p) ? p : 7340);

        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        var app = builder.Build();

        // Middleware 1: Host header validation (strictly localhost only, rejects cross-client / DNS rebinding)
        app.Use(async (context, next) =>
        {
            var hostHeader = context.Request.Host.Host;
            if (!IsLocalHost(hostHeader))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    new ApiError("FORBIDDEN", "Cross-client or non-localhost host rejected."),
                    BackendJson.Options));
                return;
            }
            await next();
        });

        // Middleware 2: Authentication enforcement for all /local/v1/ endpoints except /local/v1/health
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "";
            if (path.Equals("/local/v1/health", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            if (path.StartsWith("/local/v1/", StringComparison.OrdinalIgnoreCase))
            {
                var providedSecret = context.Request.Headers["X-Local-Agent-Secret"].ToString();
                if (string.IsNullOrWhiteSpace(providedSecret))
                {
                    var authHeader = context.Request.Headers.Authorization.ToString();
                    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        providedSecret = authHeader[7..].Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(providedSecret) || !CryptographicEquals(providedSecret, secret))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(
                        new ApiError("UNAUTHORIZED", "Valid pairing secret required."),
                        BackendJson.Options));
                    return;
                }
            }

            await next();
        });

        // Endpoint: Unauthenticated health check
        app.MapGet("/local/v1/health", () => Results.Ok(new
        {
            status = "healthy",
            service = "Gram.LocalAgent",
            version = "1.0.0",
            uptimeSeconds = (long)(DateTimeOffset.UtcNow - StartTime).TotalSeconds
        }));

        // Endpoint: capabilities.get
        app.MapGet("/local/v1/capabilities", () =>
        {
            var status = new LocalAgentStatus(
                IsAvailable: true,
                Version: "1.0.0",
                Capabilities:
                [
                    new SandboxCapability("capabilities.get", true, "Retrieve local agent version, health, and enabled capabilities."),
                    new SandboxCapability("files.pick", true, "Securely access or pick files within validated workspace boundaries."),
                    new SandboxCapability("terminal.run", true, "Execute commands within workspace scope with process-tree cancellation."),
                    new SandboxCapability("sandbox.create", true, "Create isolated sandbox workspace directory.")
                ]
            );
            return Results.Ok(status);
        });

        // Endpoint: files.pick (workspace-bounded secure file retrieval)
        app.MapPost("/local/v1/files/pick", async (FilePickRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.RelativePath))
            {
                return Results.BadRequest(new ApiError("INVALID_PATH", "Relative path is required."));
            }

            var root = string.IsNullOrWhiteSpace(request.WorkspaceRoot) 
                ? Directory.GetCurrentDirectory() 
                : Path.GetFullPath(request.WorkspaceRoot);

            if (!Directory.Exists(root))
            {
                return Results.BadRequest(new ApiError("INVALID_WORKSPACE", $"Workspace root '{root}' does not exist."));
            }

            var fullPath = Path.GetFullPath(Path.Combine(root, request.RelativePath));

            // Enforce workspace-root containment and path traversal protection
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ApiError("PATH_TRAVERSAL", "Path traversal outside the designated workspace root is forbidden."));
            }

            if (!File.Exists(fullPath))
            {
                return Results.NotFound(new ApiError("NOT_FOUND", $"File '{request.RelativePath}' was not found in workspace."));
            }

            var info = new FileInfo(fullPath);
            var maxBytes = Math.Clamp(request.MaxBytes, 1, 10_000_000);
            var toRead = (int)Math.Min(info.Length, maxBytes);

            byte[] bytes = new byte[toRead];
            await using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var read = await fs.ReadAsync(bytes.AsMemory(0, toRead));
            }

            var content = System.Text.Encoding.UTF8.GetString(bytes);
            return Results.Ok(new FilePickResult(true, fullPath, content, info.Length));
        });

        // Endpoint: terminal.run (explicit workspace scope + process tree termination)
        app.MapPost("/local/v1/terminal/run", async (TerminalRunRequest request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Command))
            {
                return Results.BadRequest(new ApiError("INVALID_COMMAND", "Command cannot be empty."));
            }

            var root = string.IsNullOrWhiteSpace(request.WorkspaceRoot)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(request.WorkspaceRoot);

            if (!Directory.Exists(root))
            {
                return Results.BadRequest(new ApiError("INVALID_WORKSPACE", $"Workspace root '{root}' does not exist."));
            }

            var timeoutSeconds = Math.Clamp(request.TimeoutSeconds, 1, 300);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var sw = Stopwatch.StartNew();
            var isWindows = OperatingSystem.IsWindows();

            var psi = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/sh",
                Arguments = isWindows ? $"/c {request.Command}" : $"-c \"{request.Command.Replace("\"", "\\\"")}\"",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            try
            {
                process.Start();

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

                await process.WaitForExitAsync(cts.Token);

                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                sw.Stop();

                var success = process.ExitCode == 0;
                long peakWorkingSet = 0;
                try { peakWorkingSet = process.PeakWorkingSet64; } catch { }

                return Results.Ok(new ExecutionResult(
                    Success: success,
                    ExitCode: process.ExitCode,
                    Stdout: stdout,
                    Stderr: stderr,
                    ElapsedMilliseconds: sw.ElapsedMilliseconds,
                    Error: success ? null : $"Command exited with code {process.ExitCode}",
                    PeakWorkingSetBytes: peakWorkingSet,
                    OutputBytes: stdout.Length + stderr.Length
                ));
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                try
                {
                    // Terminate entire process tree cleanly
                    process.Kill(entireProcessTree: true);
                }
                catch { }

                var isTimedOut = !cancellationToken.IsCancellationRequested;
                return Results.Ok(new ExecutionResult(
                    Success: false,
                    ExitCode: -1,
                    Stdout: "",
                    Stderr: isTimedOut ? $"Command timed out after {timeoutSeconds}s." : "Command was cancelled by caller.",
                    ElapsedMilliseconds: sw.ElapsedMilliseconds,
                    Error: isTimedOut ? "TIMED_OUT" : "CANCELLED"
                ));
            }
            catch (Exception ex)
            {
                sw.Stop();
                return Results.Ok(new ExecutionResult(
                    Success: false,
                    ExitCode: -1,
                    Stdout: "",
                    Stderr: ex.Message,
                    ElapsedMilliseconds: sw.ElapsedMilliseconds,
                    Error: "EXECUTION_ERROR"
                ));
            }
        });

        // Endpoint: sandbox.create
        app.MapPost("/local/v1/sandbox/create", (LocalSandboxCreateRequest request) =>
        {
            var sandboxId = "sbx-" + Guid.NewGuid().ToString("N")[..12];
            var baseDir = !string.IsNullOrWhiteSpace(request.BaseDirectory)
                ? Path.GetFullPath(request.BaseDirectory)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gram", "sandboxes");

            var sandboxPath = Path.Combine(baseDir, sandboxId);
            try
            {
                Directory.CreateDirectory(sandboxPath);
                return Results.Ok(new LocalSandboxCreateResult(true, sandboxId, sandboxPath));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ApiError("SANDBOX_FAILED", $"Could not create sandbox directory: {ex.Message}"));
            }
        });

        return app;
    }

    public static string ResolveOrCreateSecret(string? explicitSecret = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitSecret)) return explicitSecret.Trim();

        var envSecret = Environment.GetEnvironmentVariable("GRAM_LOCAL_AGENT_SECRET");
        if (!string.IsNullOrWhiteSpace(envSecret)) return envSecret.Trim();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        var dir = Path.Combine(localAppData, "Gram");
        Directory.CreateDirectory(dir);
        var secretPath = Path.Combine(dir, "local-agent.secret");

        if (File.Exists(secretPath))
        {
            var existing = File.ReadAllText(secretPath).Trim();
            if (!string.IsNullOrWhiteSpace(existing)) return existing;
        }

        var generated = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        File.WriteAllText(secretPath, generated);
        return generated;
    }

    private static bool IsLocalHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var clean = host.Trim().Trim('[', ']');
        return clean.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var bytesA = System.Text.Encoding.UTF8.GetBytes(a);
        var bytesB = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
