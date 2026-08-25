using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT") ?? "7337";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthorizationCore();

builder.Services.AddSingleton<Gram.Blazor.Shared.StorageService>();

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
builder.Services.AddSingleton<Gram.Blazor.Shared.GitService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.AgentSecurityService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.WorkspaceTabService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.ToolboxManagerService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.GramBoxWorktreeManager>();
builder.Services.AddSingleton<Gram.Blazor.Shared.NativeScratchpadDriver>();
builder.Services.AddSingleton<Gram.Blazor.Shared.GramBoxService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IGramBoxService>(sp => sp.GetRequiredService<Gram.Blazor.Shared.GramBoxService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<Gram.Blazor.Shared.GramBoxService>());
builder.Services.AddScoped<Gram.Blazor.Shared.IGramTool, Gram.Blazor.Shared.GitGramKernelTool>();
builder.Services.AddScoped<Gram.Blazor.Shared.GramToolKernel>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentToolRegistry, Gram.Blazor.Shared.AgentToolRegistry>();
builder.Services.AddScoped<Gram.Blazor.Shared.AgentOrchestrator>();
builder.Services.AddScoped<Gram.Blazor.Shared.ContextBuilder>();
builder.Services.AddSingleton<Gram.Blazor.Shared.RepositoryMapService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ThemeService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ModelService>();
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
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", version = "1.0.0", timestamp = DateTimeOffset.UtcNow }));

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
