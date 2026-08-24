using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:7337");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthorizationCore();

builder.Services.AddSingleton<Gram.Blazor.Shared.StorageService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.SqliteDatabaseService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.SessionStorageService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.MarkdownService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.GitService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.AgentSecurityService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.WorkspaceTabService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.ToolboxManagerService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IAgentToolRegistry, Gram.Blazor.Shared.AgentToolRegistry>();
builder.Services.AddScoped<Gram.Blazor.Shared.AgentOrchestrator>();
builder.Services.AddScoped<Gram.Blazor.Shared.ContextBuilder>();
builder.Services.AddScoped<Gram.Blazor.Shared.ThemeService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ModelService>();
builder.Services.AddScoped<Gram.Blazor.Shared.SettingsService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ConversationService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ChatStateService>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAuthService, Gram.Blazor.Shared.GramAuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, Gram.Blazor.Shared.GramAuthStateProvider>();
builder.Services.AddScoped<Gram.Blazor.Shared.ISonnerService, Gram.Blazor.Shared.SonnerService>();

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

app.MapRazorComponents<Gram.Blazor.Components.App>().AddInteractiveServerRenderMode();
app.Run();
