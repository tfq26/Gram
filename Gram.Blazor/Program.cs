using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

// Automatically load local .env variables if present
var possibleEnvPaths = new[] { ".env", "../.env", Path.Combine(AppContext.BaseDirectory, ".env") };
foreach (var path in possibleEnvPaths)
{
    if (File.Exists(path))
    {
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
            var parts = trimmed.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var val = parts[1].Trim().Trim('"', '\'');
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                {
                    Environment.SetEnvironmentVariable(key, val);
                }
            }
        }
        break;
    }
}

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT") ?? "7337";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<Gram.Blazor.Shared.IRateLimitCoordinator, Gram.Blazor.Shared.RateLimitCoordinator>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton<Gram.Blazor.Shared.IStreamingTranscriberFactory, Gram.Blazor.Shared.AssemblyAiStreamingTranscriberFactory>();
builder.Services.AddSingleton<Gram.Blazor.Shared.ITextToSpeechProvider, Gram.Blazor.Shared.ElevenLabsTextToSpeechProvider>();

builder.Services.AddSingleton<Gram.Blazor.Shared.StorageService>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentRunTracker, Gram.Blazor.Shared.AgentRunTracker>();

var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL") 
    ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

if (!string.IsNullOrWhiteSpace(databaseUrl))
{
    builder.Services.AddSingleton<Gram.Blazor.Shared.IDatabaseProvider>(sp => 
        new Gram.Blazor.Shared.PostgresDatabaseProvider(
            sp.GetRequiredService<ILogger<Gram.Blazor.Shared.PostgresDatabaseProvider>>(),
            databaseUrl));
}
else
{
    builder.Services.AddSingleton<Gram.Blazor.Shared.SqliteDatabaseService>();
    builder.Services.AddSingleton<Gram.Blazor.Shared.IDatabaseProvider>(sp => sp.GetRequiredService<Gram.Blazor.Shared.SqliteDatabaseService>());
}
builder.Services.AddSingleton<Gram.Blazor.Shared.SessionStorageService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.MarkdownService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IWebSearchProvider, Gram.Blazor.Shared.ParallelWebSearchProvider>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IWebSearchProvider, Gram.Blazor.Shared.TavilyWebSearchProvider>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IImageSearchProvider, Gram.Blazor.Shared.WikimediaImageSearchProvider>();
builder.Services.AddSingleton<Gram.Blazor.Shared.WebContentService>();
builder.Services.AddScoped<Gram.Blazor.Shared.SearchGroundingHarness>();
builder.Services.AddScoped<Gram.Blazor.Shared.SearchDiagnosticsLogService>();
builder.Services.AddScoped<Gram.Blazor.Shared.SummaryDiagnosticsLogService>();
builder.Services.AddScoped<Gram.Blazor.Shared.PresentationImageAssetService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.GitService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.AgentSecurityService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.WorkspaceTabService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.ToolboxManagerService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.MentionResolver>();
builder.Services.AddSingleton<Gram.Blazor.Shared.GramBoxWorktreeManager>();
builder.Services.AddSingleton<Gram.Blazor.Shared.ISandboxScheduler, Gram.Blazor.Shared.SandboxScheduler>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IPlatformSandboxController, Gram.Blazor.Shared.PlatformSandboxController>();
builder.Services.AddSingleton<Gram.Blazor.Shared.ISandboxMergeCoordinator, Gram.Blazor.Shared.SandboxMergeCoordinator>();
builder.Services.AddSingleton<Gram.Blazor.Shared.ISandboxCheckpointService, Gram.Blazor.Shared.SandboxCheckpointService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.NativeScratchpadDriver>();
builder.Services.AddSingleton<Gram.Blazor.Shared.GramBoxService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IGramBoxService>(sp => sp.GetRequiredService<Gram.Blazor.Shared.GramBoxService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<Gram.Blazor.Shared.GramBoxService>());
builder.Services.AddHostedService<Gram.Blazor.Shared.SandboxCleanupService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ISubagentEngine, Gram.Blazor.Shared.SubagentEngineService>();
builder.Services.AddScoped<Gram.Blazor.Shared.IQuotaTracker, Gram.Blazor.Shared.QuotaTracker>();
builder.Services.AddScoped<Gram.Blazor.Shared.PresentationGenerationService>();
builder.Services.AddScoped<Gram.Blazor.Shared.HtmlPresentationRenderer>();
builder.Services.AddScoped<Gram.Blazor.Shared.IGramTool, Gram.Blazor.Shared.GitGramKernelTool>();
builder.Services.AddScoped<Gram.Blazor.Shared.GramToolKernel>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentToolRegistry, Gram.Blazor.Shared.AgentToolRegistry>();
builder.Services.AddScoped<Gram.Blazor.Shared.IToolExecutor, Gram.Blazor.Shared.ToolExecutor>();
builder.Services.AddScoped<Gram.Blazor.Shared.AgentOrchestrator>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentWorkflow, Gram.Blazor.Shared.ChatWorkflow>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentWorkflow, Gram.Blazor.Shared.ResearchWorkflow>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentWorkflow, Gram.Blazor.Shared.PresentationWorkflow>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentWorkflowRouter, Gram.Blazor.Shared.AgentWorkflowRouter>();
builder.Services.AddScoped<Gram.Blazor.Shared.ContextBuilder>();
builder.Services.AddSingleton<Gram.Blazor.Shared.RepositoryMapService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ThemeService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ModelService>();
builder.Services.AddScoped<Gram.Blazor.Shared.IModelGateway, Gram.Blazor.Shared.ModelGateway>();
builder.Services.AddScoped<Gram.Blazor.Shared.SettingsService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ConversationService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ChatStateService>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAuthService, Gram.Blazor.Shared.GramAuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, Gram.Blazor.Shared.GramAuthStateProvider>();
builder.Services.AddAuthentication();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<Gram.Blazor.Shared.ISonnerService, Gram.Blazor.Shared.SonnerService>();
builder.Services.AddScoped<Gram.Blazor.Shared.GitGramService>();

var app = builder.Build();
app.UseWebSockets();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", version = "1.0.0", timestamp = DateTimeOffset.UtcNow }));

app.Map("/api/voice/assemblyai", async context =>
{
    var factory = context.RequestServices.GetRequiredService<Gram.Blazor.Shared.IStreamingTranscriberFactory>();
    if (!factory.IsConfigured)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = "AssemblyAI is not configured." });
        return;
    }

    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var browserSocket = await context.WebSockets.AcceptWebSocketAsync();
    await using var transcriber = factory.Create();
    using var browserSendLock = new SemaphoreSlim(1, 1);

    async Task SendToBrowserAsync(object message)
    {
        if (browserSocket.State != WebSocketState.Open) return;

        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await browserSendLock.WaitAsync(context.RequestAborted);
        try
        {
            if (browserSocket.State == WebSocketState.Open)
                await browserSocket.SendAsync(payload, WebSocketMessageType.Text, true, context.RequestAborted);
        }
        finally
        {
            browserSendLock.Release();
        }
    }

    transcriber.TranscriptReceived += update => SendToBrowserAsync(new
    {
        type = "transcript",
        text = update.Text,
        isFinal = update.IsFinal,
        provider = update.Provider
    });

    try
    {
        await transcriber.ConnectAsync(context.RequestAborted);
        await SendToBrowserAsync(new { type = "status", status = "connected", provider = factory.ProviderName });

        var buffer = new byte[32 * 1024];
        using var audioMessage = new MemoryStream();
        while (browserSocket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
        {
            var result = await browserSocket.ReceiveAsync(buffer, context.RequestAborted);
            if (result.MessageType == WebSocketMessageType.Close) break;

            audioMessage.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var command = Encoding.UTF8.GetString(audioMessage.GetBuffer(), 0, checked((int)audioMessage.Length));
                audioMessage.SetLength(0);
                if (command.Contains("terminate", StringComparison.OrdinalIgnoreCase)) break;
                continue;
            }

            var audio = audioMessage.ToArray();
            audioMessage.SetLength(0);
            await transcriber.SendAudioAsync(audio, context.RequestAborted);
        }

        await transcriber.CompleteAsync(context.RequestAborted);
    }
    catch (OperationCanceledException) {}
    catch (Exception ex)
    {
        try { await SendToBrowserAsync(new { type = "error", message = ex.Message }); }
        catch {}
    }
});

app.MapPost("/api/tts/stream", async (
    Gram.Blazor.Shared.TextToSpeechRequest request,
    Gram.Blazor.Shared.ITextToSpeechProvider textToSpeech,
    CancellationToken cancellationToken) =>
{
    if (!textToSpeech.IsConfigured)
        return Results.Problem("ElevenLabs is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

    if (string.IsNullOrWhiteSpace(request.Text))
        return Results.BadRequest(new { error = "Text is required." });

    var audioStream = await textToSpeech.SynthesizeAsync(request, cancellationToken);
    return Results.Stream(audioStream, "audio/mpeg");
});

app.MapGet("/api/tts/voices", (Gram.Blazor.Shared.ITextToSpeechProvider textToSpeech) =>
    Results.Ok(new
    {
        provider = textToSpeech.ProviderName,
        configured = textToSpeech.IsConfigured,
        defaultVoiceId = textToSpeech.DefaultVoiceId,
        voices = textToSpeech.Voices
    }));

app.MapGet("/api/logs", (Gram.Blazor.Shared.StorageService storage) =>
{
    var directory = storage.LogsDirectory;
    if (!Directory.Exists(directory)) return Results.Ok(Array.Empty<object>());
    var files = Directory.GetFiles(directory, "gram*.log")
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .Take(4)
        .Select(path => new { name = Path.GetFileName(path), size = new FileInfo(path).Length, modified = File.GetLastWriteTimeUtc(path) });
    return Results.Ok(files);
});

app.MapGet("/api/logs/{name}", (string name, Gram.Blazor.Shared.StorageService storage) =>
{
    var safeName = Path.GetFileName(name);
    var path = Path.Combine(storage.LogsDirectory, safeName);
    return File.Exists(path) ? Results.File(path, "text/plain", safeName) : Results.NotFound();
});

app.MapGet("/api/models/health", async (Gram.Blazor.Shared.ModelService modelService) =>
{
    var results = new Dictionary<string, object>();
    var openRouterHealth = await modelService.CheckProviderHealthAsync("openrouter");
    var ollamaHealth = await modelService.CheckProviderHealthAsync("ollama");

    results["openrouter"] = openRouterHealth;
    results["ollama"] = ollamaHealth;
    return Results.Ok(results);
});

app.MapGet("/api/models/health/{provider}", async (string provider, Gram.Blazor.Shared.ModelService modelService) =>
{
    var health = await modelService.CheckProviderHealthAsync(provider);
    return Results.Ok(health);
});

app.MapGet("/api/grambox/status", (Gram.Blazor.Shared.IGramBoxService gramBox) =>
{
    return Results.Ok(new
    {
        engine = gramBox.ActiveEngine.ToString(),
        status = gramBox.StatusMessage,
        isReady = gramBox.IsReady,
        activeSandboxes = gramBox.ActiveSessions
    });
});

app.MapPost("/api/grambox/test", async (Gram.Blazor.Shared.IGramBoxService gramBox) =>
{
    var logs = new List<string>();
    try
    {
        logs.Add("[1] Creating test worktree sandbox...");
        var session = await gramBox.CreateSandboxAsync(Environment.CurrentDirectory, "gram/test-auth-spike", "Test Auth Spike");
        logs.Add($"    Created ID: {session.Id} on branch {session.BranchName}");

        logs.Add("[2] Executing command in sandbox (git status)...");
        var statusRes = await gramBox.ExecuteAsync("git status --short", session.Id);
        logs.Add($"    Exit Code: {statusRes.ExitCode} | Output: {(string.IsNullOrWhiteSpace(statusRes.Stdout) ? "Clean" : statusRes.Stdout)}");

        logs.Add("[3] Writing test file inside sandbox worktree...");
        var writeOk = await gramBox.WriteFileAsync("Shared/SandboxVerification.cs", "// Verified autonomous execution\n", session.Id);
        logs.Add($"    Write success: {writeOk}");

        logs.Add("[4] Extracting git diff from sandbox worktree...");
        var diff = await gramBox.GetDiffAsync(session.Id);
        logs.Add($"    Diff: {diff.FilesChanged} files changed (+{diff.Insertions} / -{diff.Deletions})");

        logs.Add("[5] Committing changes in sandbox worktree...");
        var commitOk = await gramBox.CommitChangesAsync("feat: test autonomous sandbox commit", session.Id);
        logs.Add($"    Commit success: {commitOk}");

        logs.Add("[6] Closing and pruning sandbox worktree...");
        var closeOk = await gramBox.CloseSandboxAsync(session.Id);
        logs.Add($"    Cleaned up: {closeOk}");

        return Results.Ok(new { success = true, logs });
    }
    catch (Exception ex)
    {
        logs.Add($"[Error] {ex.Message}");
        return Results.Json(new { success = false, logs, error = ex.Message }, statusCode: 500);
    }
});

app.MapRazorComponents<Gram.Blazor.Components.App>().AddInteractiveServerRenderMode();

var dbProvider = app.Services.GetRequiredService<Gram.Blazor.Shared.IDatabaseProvider>();
await dbProvider.InitializeAsync();

app.Run();
