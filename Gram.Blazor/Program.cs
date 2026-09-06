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
builder.Services.AddSingleton<Gram.Blazor.Shared.IObjectStorageService, Gram.Blazor.Shared.S3ObjectStorageService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.DocumentParserService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.SandboxAuditService>();
builder.Services.AddScoped<Gram.Blazor.Shared.SandboxBenchmarkService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IAgentRunTracker, Gram.Blazor.Shared.AgentRunTracker>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, Gram.Blazor.Shared.GramCircuitHandler>();

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
builder.Services.AddSingleton<Gram.Blazor.Shared.IEmailService, Gram.Blazor.Shared.EmailService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.ITaskAndMemoryService, Gram.Blazor.Shared.TaskAndMemoryService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IStructuredMemoryService, Gram.Blazor.Shared.StructuredMemoryService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.MemoryExtractionService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.SessionStorageService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.MarkdownService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IWebSearchProvider, Gram.Blazor.Shared.ParallelWebSearchProvider>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IWebSearchProvider, Gram.Blazor.Shared.TavilyWebSearchProvider>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IImageSearchProvider, Gram.Blazor.Shared.WikimediaImageSearchProvider>();
builder.Services.AddSingleton<Gram.Blazor.Shared.WebContentService>();
builder.Services.AddScoped<Gram.Blazor.Shared.SearchGroundingHarness>();
builder.Services.AddSingleton<Gram.Blazor.Shared.ResearchEvidenceService>();
builder.Services.AddScoped<Gram.Blazor.Shared.SearchDiagnosticsLogService>();
builder.Services.AddScoped<Gram.Blazor.Shared.SummaryDiagnosticsLogService>();
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
builder.Services.AddSingleton<Gram.Blazor.Shared.ISandboxAccessPolicy, Gram.Blazor.Shared.SandboxAccessPolicy>();
builder.Services.AddSingleton<Gram.Blazor.Shared.SandboxJobQueue>();
builder.Services.AddSingleton<Gram.Blazor.Shared.ISandboxJobQueue>(sp => sp.GetRequiredService<Gram.Blazor.Shared.SandboxJobQueue>());
builder.Services.AddHostedService<Gram.Blazor.Shared.SandboxJobWorker>();
var redisConnection = Environment.GetEnvironmentVariable("GRAM_REDIS_URL") ?? Environment.GetEnvironmentVariable("REDIS_URL");
if (string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddSingleton<Gram.Blazor.Shared.ISandboxMessageBus, Gram.Blazor.Shared.InProcessSandboxMessageBus>();
    builder.Services.AddSingleton<Gram.Blazor.Shared.IGramCache, Gram.Blazor.Shared.InMemoryGramCache>();
}
else
{
    builder.Services.AddSingleton<Gram.Blazor.Shared.ISandboxMessageBus>(_ => new Gram.Blazor.Shared.RedisSandboxMessageBus(redisConnection));
    builder.Services.AddSingleton<Gram.Blazor.Shared.IGramCache>(_ => new Gram.Blazor.Shared.RedisGramCache(redisConnection));
}
builder.Services.AddHostedService<Gram.Blazor.Shared.SandboxOutboxProcessor>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IGramBoxService>(sp => sp.GetRequiredService<Gram.Blazor.Shared.GramBoxService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<Gram.Blazor.Shared.GramBoxService>());
builder.Services.AddHostedService<Gram.Blazor.Shared.SandboxCleanupService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ISubagentEngine, Gram.Blazor.Shared.SubagentEngineService>();
builder.Services.AddScoped<Gram.Blazor.Shared.IQuotaTracker, Gram.Blazor.Shared.QuotaTracker>();
builder.Services.AddScoped<Gram.Blazor.Shared.IGramTool, Gram.Blazor.Shared.GitGramKernelTool>();
builder.Services.AddScoped<Gram.Blazor.Shared.GramToolKernel>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentToolRegistry, Gram.Blazor.Shared.AgentToolRegistry>();
builder.Services.AddScoped<Gram.Blazor.Shared.IToolExecutor, Gram.Blazor.Shared.ToolExecutor>();
builder.Services.AddScoped<Gram.Blazor.Shared.AgentOrchestrator>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentWorkflow, Gram.Blazor.Shared.ChatWorkflow>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentWorkflow, Gram.Blazor.Shared.ResearchWorkflow>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentWorkflow, Gram.Blazor.Shared.CodingWorkflow>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentWorkflow, Gram.Blazor.Shared.VoiceWorkflow>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAgentWorkflowRouter, Gram.Blazor.Shared.AgentWorkflowRouter>();
builder.Services.AddScoped<Gram.Blazor.Shared.ContextBuilder>();
builder.Services.AddSingleton<Gram.Blazor.Shared.RepositoryMapService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ThemeService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ModelService>();
builder.Services.AddScoped<Gram.Blazor.Shared.IModelGateway, Gram.Blazor.Shared.ModelGateway>();
builder.Services.AddScoped<Gram.Blazor.Shared.SettingsService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ProjectService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ConversationService>();
builder.Services.AddScoped<Gram.Blazor.Shared.ChatStateService>();
builder.Services.AddScoped<Gram.Blazor.Shared.IAuthService, Gram.Blazor.Shared.GramAuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, Gram.Blazor.Shared.GramAuthStateProvider>();
builder.Services.AddAuthentication();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<Gram.Blazor.Shared.ISonnerService, Gram.Blazor.Shared.SonnerService>();
builder.Services.AddScoped<Gram.Blazor.Shared.GitGramService>();
builder.Services.AddScoped<Gram.Blazor.Shared.BackendFacade>();
builder.Services.AddScoped<Gram.Blazor.Shared.IConversationApi>(sp => sp.GetRequiredService<Gram.Blazor.Shared.BackendFacade>());
builder.Services.AddScoped<Gram.Blazor.Shared.IConversationRepository, Gram.Blazor.Shared.ConversationRepository>();
builder.Services.AddScoped<Gram.Blazor.Shared.ChatSessionController>();
builder.Services.AddSingleton<Gram.Blazor.Shared.RunManager>();
builder.Services.AddScoped<Gram.Blazor.Shared.IRunApi>(sp => sp.GetRequiredService<Gram.Blazor.Shared.BackendFacade>());
builder.Services.AddScoped<Gram.Blazor.Shared.IModelApi>(sp => sp.GetRequiredService<Gram.Blazor.Shared.BackendFacade>());
builder.Services.AddScoped<Gram.Blazor.Shared.ISearchApi>(sp => sp.GetRequiredService<Gram.Blazor.Shared.BackendFacade>());
builder.Services.AddScoped<Gram.Blazor.Shared.IArtifactApi>(sp => sp.GetRequiredService<Gram.Blazor.Shared.BackendFacade>());
builder.Services.AddScoped<Gram.Blazor.Shared.ILocalAgentApi>(sp => sp.GetRequiredService<Gram.Blazor.Shared.BackendFacade>());
builder.Services.AddSingleton<Gram.Blazor.Shared.IActionPermissionService, Gram.Blazor.Shared.ConnectorPermissionService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectorRegistry>(sp => new Gram.Blazor.Shared.ConnectorRegistry(sp.GetServices<Gram.Blazor.Shared.IConnectedConnector>()));
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectorSdk, Gram.Blazor.Shared.ConnectorSdk>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectorPackageService, Gram.Blazor.Shared.ConnectorPackageService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectorRuntimeService, Gram.Blazor.Shared.ConnectorRuntimeService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.ICapabilityIntentRouter, Gram.Blazor.Shared.CapabilityIntentRouter>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectorCredentialVault, Gram.Blazor.Shared.ConnectorCredentialVault>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectorHostClient, Gram.Blazor.Shared.ConnectorHostClient>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectorConnectionStore, Gram.Blazor.Shared.ConnectorConnectionStore>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectorConnectionResolver, Gram.Blazor.Shared.ConnectorConnectionResolver>();
builder.Services.AddSingleton<Gram.Blazor.Shared.SpotifyOAuthService>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectedConnector, Gram.Blazor.Shared.ReminderConnector>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectedConnector, Gram.Blazor.Shared.ControlledHttpConnector>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectedConnector, Gram.Blazor.Shared.DesktopUseConnector>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectedConnector, Gram.Blazor.Shared.SpotifyConnector>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectedConnector, Gram.Blazor.Shared.HomeAssistantConnector>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectedConnector, Gram.Blazor.Shared.PostmanConnector>();
builder.Services.AddSingleton<Gram.Blazor.Shared.IConnectedConnector, Gram.Blazor.Shared.McpBridgeConnector>();

var app = builder.Build();
app.UseWebSockets();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", version = "1.0.0", timestamp = DateTimeOffset.UtcNow }));

var api = app.MapGroup("/api/v1").AddEndpointFilter<Gram.Blazor.Shared.BackendAuthorizationFilter>();
api.MapPost("/conversations", async (Gram.Blazor.Shared.CreateConversationRequest request, Gram.Blazor.Shared.IConversationApi conversations, CancellationToken ct) => Results.Ok(await conversations.CreateAsync(request, ct)));
api.MapGet("/conversations", async (Gram.Blazor.Shared.IConversationApi conversations, CancellationToken ct) => Results.Ok(await conversations.ListAsync(ct)));
api.MapGet("/conversations/{id}", async (string id, Gram.Blazor.Shared.IConversationApi conversations, CancellationToken ct) => (await conversations.GetAsync(id, ct)) is { } result ? Results.Ok(result) : Results.NotFound(new Gram.Blazor.Shared.ApiError("conversation_not_found", "Conversation was not found.")));
api.MapPut("/conversations/{id}", async (string id, Gram.Blazor.Shared.SaveConversationRequest request, Gram.Blazor.Shared.IConversationApi conversations, CancellationToken ct) =>
{
    try { return (await conversations.SaveAsync(id, request, ct)) is { } result ? Results.Ok(result) : Results.NotFound(new Gram.Blazor.Shared.ApiError("conversation_not_found", "Conversation was not found.")); }
    catch (Gram.Blazor.Shared.ConversationVersionConflictException ex) { return Results.Conflict(new Gram.Blazor.Shared.ApiError("version_conflict", ex.Message, Details: new Dictionary<string, string[]> { ["currentVersion"] = [ex.CurrentVersion.ToString()] })); }
});
api.MapDelete("/conversations/{id}", async (string id, Gram.Blazor.Shared.IConversationApi conversations, CancellationToken ct) => await conversations.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound(new Gram.Blazor.Shared.ApiError("conversation_not_found", "Conversation was not found.")));
api.MapPost("/conversations/{id}/runs", async (string id, Gram.Blazor.Shared.SendMessageRequest request, Gram.Blazor.Shared.IRunApi runs, CancellationToken ct) =>
{
    try { var result = await runs.StartAsync(id, request, ct); return Results.Accepted($"/api/v1/runs/{result.Id}", result); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new Gram.Blazor.Shared.ApiError("conversation_not_found", ex.Message)); }
    catch (ArgumentException ex) { return Results.BadRequest(new Gram.Blazor.Shared.ApiError("invalid_request", ex.Message)); }
});
api.MapGet("/runs/{id}", (string id, Gram.Blazor.Shared.IRunApi runs) => runs.Get(id) is { } result ? Results.Ok(result) : Results.NotFound(new Gram.Blazor.Shared.ApiError("run_not_found", "Run was not found.")));
api.MapPost("/runs/{id}/cancel", async (string id, Gram.Blazor.Shared.CancelRunRequest request, Gram.Blazor.Shared.IRunApi runs) => await runs.CancelAsync(id, request.Reason) ? Results.Accepted() : Results.NotFound(new Gram.Blazor.Shared.ApiError("run_not_found", "Run was not found.")));
api.MapGet("/runs/{id}/events", async (HttpContext context, string id, Gram.Blazor.Shared.IRunApi runs, CancellationToken ct) =>
{
    if (runs.Get(id) is null) { context.Response.StatusCode = 404; return; }
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    var last = long.TryParse(context.Request.Headers["Last-Event-ID"].FirstOrDefault(), out var headerSequence) ? headerSequence : 0;
    while (!ct.IsCancellationRequested)
    {
        foreach (var item in runs.GetEvents(id, last))
        {
            last = item.Sequence;
            await context.Response.WriteAsync($"id: {item.Sequence}\nevent: {item.EventType}\ndata: {System.Text.Json.JsonSerializer.Serialize(item, Gram.Blazor.Shared.BackendJson.Options)}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
        if (runs.Get(id)?.CompletedAt is not null) break;
        await Task.Delay(250, ct);
    }
});
api.MapGet("/models", (Gram.Blazor.Shared.IModelApi models) => Results.Ok(models.ListModels()));
api.MapGet("/providers", (Gram.Blazor.Shared.IModelApi models) => Results.Ok(models.ListProviders()));
api.MapGet("/connectors", (Gram.Blazor.Shared.IConnectorRegistry registry) => Results.Ok(registry.ListConnectors()));
api.MapGet("/connectors/{id}/actions", (string id, Gram.Blazor.Shared.IConnectorRegistry registry) => Results.Ok(registry.ListActions(id)));
api.MapGet("/capabilities/resolve", (string q, Gram.Blazor.Shared.ICapabilityIntentRouter router) => Results.Ok(router.Resolve(q)));
api.MapPost("/actions/preview", (Gram.Blazor.Shared.ConnectorActionRequest request, Gram.Blazor.Shared.IConnectorRegistry registry, Gram.Blazor.Shared.IActionPermissionService permissions) =>
{
    var action = registry.ListActions(request.ConnectorId).FirstOrDefault(a => a.Id.Equals(request.ActionId, StringComparison.OrdinalIgnoreCase));
    return action is null ? Results.NotFound(new Gram.Blazor.Shared.ApiError("action_not_found", "Connector action was not found.")) : Results.Ok(new { action, riskLevel = permissions.Classify(action), requiresApproval = permissions.Classify(action) != Gram.Blazor.Shared.ActionRiskLevel.Safe });
});
api.MapPost("/actions/execute", async (HttpContext context, Gram.Blazor.Shared.ConnectorActionRequest request, Gram.Blazor.Shared.IConnectorRegistry registry, Gram.Blazor.Shared.IActionPermissionService permissions, Gram.Blazor.Shared.IConnectorConnectionResolver connections, Gram.Blazor.Shared.SpotifyOAuthService spotify, CancellationToken ct) =>
{
    var connector = registry.Get(request.ConnectorId);
    var action = registry.ListActions(request.ConnectorId).FirstOrDefault(a => a.Id.Equals(request.ActionId, StringComparison.OrdinalIgnoreCase));
    if (connector is null || action is null) return Results.NotFound(new Gram.Blazor.Shared.ApiError("action_not_found", "Connector action was not found."));
    var userId = GetMemoryUserId(context);
    request = request with { UserId = userId };
    var connection = await connections.ResolveAsync(userId, connector.Descriptor, action, request.ConnectionId, ct);
    if (connection.Status == Gram.Blazor.Shared.ConnectionResolutionStatus.ReauthorizationRequired && request.ConnectorId.Equals("spotify", StringComparison.OrdinalIgnoreCase) && connection.Connection != null)
    {
        try
        {
            await spotify.RefreshAsync(userId, connection.Connection.Id, ct);
            connection = await connections.ResolveAsync(userId, connector.Descriptor, action, connection.Connection.Id, ct);
        }
        catch { }
    }
    if (connection.Status != Gram.Blazor.Shared.ConnectionResolutionStatus.Ready) return Results.Ok(new Gram.Blazor.Shared.ConnectorActionResult(false, connection.Status.ToString(), connection.Message));
    request = request with { ConnectionId = connection.Connection?.Id ?? request.ConnectionId };
    if (permissions.Classify(action) != Gram.Blazor.Shared.ActionRiskLevel.Safe && await permissions.GetApprovalAsync(userId, request, ct) is null)
        return Results.Ok(new Gram.Blazor.Shared.ConnectorActionResult(false, "approval_required", "This action requires user approval.", RequiresApproval: true));
    return Results.Ok(await connector.ExecuteAsync(request, ct));
});
api.MapGet("/connectors/connections", async (HttpContext context, string? connectorId, Gram.Blazor.Shared.IConnectorConnectionStore connections, CancellationToken ct) => Results.Ok(await connections.ListAsync(GetMemoryUserId(context), connectorId, ct)));
api.MapGet("/connectors/spotify/authorize", (HttpContext context, Gram.Blazor.Shared.SpotifyOAuthService spotify) => Results.Redirect(spotify.Start(GetMemoryUserId(context), context.Request.Query["returnUrl"].FirstOrDefault()).AuthorizationUrl));
api.MapGet("/connectors/spotify/authorize-url", (HttpContext context, Gram.Blazor.Shared.SpotifyOAuthService spotify) => Results.Ok(spotify.Start(GetMemoryUserId(context), context.Request.Query["returnUrl"].FirstOrDefault())));
api.MapGet("/connectors/spotify/callback", async (string? code, string? state, string? error, Gram.Blazor.Shared.SpotifyOAuthService spotify, CancellationToken ct) =>
{
    if (!string.IsNullOrWhiteSpace(error)) return Results.BadRequest(new Gram.Blazor.Shared.ApiError("spotify_authorization_denied", error));
    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) return Results.BadRequest(new Gram.Blazor.Shared.ApiError("spotify_callback_invalid", "Spotify authorization callback was incomplete."));
    var completed = await spotify.CompleteAsync(code, state, ct);
    return string.IsNullOrWhiteSpace(completed.ReturnUrl) ? Results.Ok(completed.Connection) : Results.Redirect(completed.ReturnUrl);
});
api.MapPost("/connectors/spotify/{connectionId}/refresh", async (HttpContext context, string connectionId, Gram.Blazor.Shared.SpotifyOAuthService spotify, CancellationToken ct) => Results.Ok(await spotify.RefreshAsync(GetMemoryUserId(context), connectionId, ct)));
api.MapDelete("/connectors/spotify/{connectionId}", async (HttpContext context, string connectionId, Gram.Blazor.Shared.SpotifyOAuthService spotify, CancellationToken ct) => await spotify.DisconnectAsync(GetMemoryUserId(context), connectionId, ct) ? Results.NoContent() : Results.NotFound());
api.MapPost("/connectors/connections", async (HttpContext context, Gram.Blazor.Shared.ConnectorConnectionRegistrationRequest request, Gram.Blazor.Shared.IConnectorConnectionStore connections, CancellationToken ct) =>
{
    var connection = new Gram.Blazor.Shared.ConnectorConnection(Guid.NewGuid().ToString("N"), request.ConnectorId, request.DisplayName, request.Scopes, true, request.ExpiresAt, GetMemoryUserId(context), request.AuthenticationType, DateTimeOffset.UtcNow, request.ClientId);
    return Results.Ok(await connections.SaveAsync(connection, ct));
});
api.MapPost("/actions/approve", async (HttpContext context, Gram.Blazor.Shared.RecordActionApprovalRequest request, Gram.Blazor.Shared.IActionPermissionService permissions, CancellationToken ct) => Results.Ok(await permissions.RecordApprovalAsync(GetMemoryUserId(context), request.Request, request.Decision, request.Scope, ct)));
api.MapPost("/connectors/generate/preview", async (Gram.Blazor.Shared.ConnectorGenerationRequest request, Gram.Blazor.Shared.IConnectorSdk sdk, CancellationToken ct) =>
    Results.Ok(await sdk.GeneratePreviewAsync(request, ct)));
api.MapPost("/connectors/packages/stage", async (Gram.Blazor.Shared.ConnectorGenerationPreview preview, Gram.Blazor.Shared.IConnectorPackageService packages, CancellationToken ct) => Results.Ok(await packages.StageAsync(preview, ct)));
api.MapPost("/connectors/packages/{id}/compile", async (string id, Gram.Blazor.Shared.IConnectorPackageService packages, CancellationToken ct) => Results.Ok(await packages.CompileAsync(id, ct)));
api.MapPost("/connectors/packages/{id}/install", async (string id, Gram.Blazor.Shared.InstallConnectorPackageRequest request, Gram.Blazor.Shared.IConnectorPackageService packages, CancellationToken ct) => Results.Ok(await packages.InstallAsync(id, request.Approved, ct)));
api.MapGet("/connectors/packages", async (Gram.Blazor.Shared.IConnectorPackageService packages, CancellationToken ct) => Results.Ok(await packages.ListAsync(ct)));
api.MapPost("/connectors/packages/{id}/activate", async (string id, Gram.Blazor.Shared.IConnectorRuntimeService runtime, CancellationToken ct) => Results.Ok(await runtime.ActivateAsync(id, ct)));
api.MapGet("/connectors/runtime", (Gram.Blazor.Shared.IConnectorRuntimeService runtime) => Results.Ok(runtime.List()));
api.MapGet("/connectors/{id}/health", async (string id, Gram.Blazor.Shared.IConnectorRuntimeService runtime, CancellationToken ct) => Results.Ok(await runtime.HealthAsync(id, ct)));
api.MapPost("/connectors/{id}/disable", async (string id, Gram.Blazor.Shared.IConnectorRuntimeService runtime, CancellationToken ct) => await runtime.DisableAsync(id, ct) ? Results.Ok() : Results.NotFound());
api.MapPost("/connectors/{id}/rollback", async (string id, Gram.Blazor.Shared.IConnectorRuntimeService runtime, CancellationToken ct) => Results.Ok(await runtime.RollbackAsync(id, ct)));
api.MapGet("/memory", async (HttpContext context, Gram.Blazor.Shared.IStructuredMemoryService memory, CancellationToken ct) =>
    Results.Ok(await memory.ListAsync(GetMemoryUserId(context), includeInactive: true, cancellationToken: ct)));
api.MapGet("/memory/search", async (HttpContext context, string q, string? projectId, Gram.Blazor.Shared.IStructuredMemoryService memory, CancellationToken ct) =>
    Results.Ok(await memory.SearchAsync(new Gram.Blazor.Shared.MemorySearchRequest(q, GetMemoryUserId(context), projectId), ct)));
api.MapPost("/memory", async (HttpContext context, Gram.Blazor.Shared.RememberMemoryRequest request, Gram.Blazor.Shared.IStructuredMemoryService memory, CancellationToken ct) =>
    Results.Ok(await memory.RememberAsync(request with { UserId = GetMemoryUserId(context) }, ct)));
api.MapPost("/memory/{id}/confirm", async (HttpContext context, string id, Gram.Blazor.Shared.IStructuredMemoryService memory, CancellationToken ct) =>
    await memory.ConfirmAsync(id, GetMemoryUserId(context), ct) ? Results.Ok() : Results.NotFound(new Gram.Blazor.Shared.ApiError("memory_not_found", "Candidate memory was not found.")));
api.MapPost("/memory/{id}/reject", async (HttpContext context, string id, Gram.Blazor.Shared.IStructuredMemoryService memory, CancellationToken ct) =>
    await memory.RejectAsync(id, GetMemoryUserId(context), ct) ? Results.Ok() : Results.NotFound(new Gram.Blazor.Shared.ApiError("memory_not_found", "Candidate memory was not found.")));
api.MapDelete("/memory/{id}", async (HttpContext context, string id, Gram.Blazor.Shared.IStructuredMemoryService memory, CancellationToken ct) =>
    await memory.ForgetAsync(id, GetMemoryUserId(context), ct) ? Results.NoContent() : Results.NotFound(new Gram.Blazor.Shared.ApiError("memory_not_found", "Memory was not found.")));
api.MapPost("/search", async (Gram.Blazor.Shared.SearchRequest request, Gram.Blazor.Shared.ISearchApi search, CancellationToken ct) => Results.Ok(await search.SearchAsync(request, ct)));
api.MapPost("/research", async (Gram.Blazor.Shared.ResearchRequest request, Gram.Blazor.Shared.ResearchEvidenceService research, CancellationToken ct) => Results.Ok(await research.BuildAsync(request, ct)));
api.MapGet("/artifacts/{id}", async (string id, Gram.Blazor.Shared.IArtifactApi artifacts, CancellationToken ct) => (await artifacts.GetAsync(id, ct)) is { } result ? Results.Ok(result) : Results.NotFound(new Gram.Blazor.Shared.ApiError("artifact_not_found", "Artifact was not found.")));
api.MapGet("/artifacts/{id}/content", async (string id, Gram.Blazor.Shared.IArtifactApi artifacts, CancellationToken ct) => (await artifacts.GetContentAsync(id, ct)) is { } result ? Results.Ok(result) : Results.NotFound(new Gram.Blazor.Shared.ApiError("artifact_not_found", "Artifact was not found.")));
api.MapGet("/local-agent/status", (Gram.Blazor.Shared.ILocalAgentApi agent) => Results.Ok(agent.GetStatus()));
api.MapGet("/local-agent/capabilities", (Gram.Blazor.Shared.ILocalAgentApi agent) => Results.Ok(agent.GetStatus().Capabilities ?? []));
api.MapGet("/permissions/pending", (Gram.Blazor.Shared.AgentSecurityService security) => Results.Ok(security.GetPendingRequests()));
api.MapPost("/permissions/{id}/respond", (string id, Gram.Blazor.Shared.PermissionResponseRequest request, Gram.Blazor.Shared.AgentSecurityService security) =>
{
    var success = security.RespondApproval(id, request.Approved, request.RememberForSession);
    return success ? Results.Ok(new { success = true }) : Results.NotFound(new Gram.Blazor.Shared.ApiError("permission_not_found", "Approval request expired or not found."));
});
api.MapGet("/security/settings", (Gram.Blazor.Shared.AgentSecurityService security) => Results.Ok(security.Settings));
api.MapPost("/security/settings", (Gram.Blazor.Shared.SecurityGuardSettings settings, Gram.Blazor.Shared.AgentSecurityService security) =>
{
    security.Settings.PermissionMode = settings.PermissionMode;
    security.Settings.BlockDestructiveCommands = settings.BlockDestructiveCommands;
    security.Settings.EnforceWorkspaceSandboxing = settings.EnforceWorkspaceSandboxing;
    security.Settings.CommandTimeoutSeconds = settings.CommandTimeoutSeconds;
    security.SaveSettings();
    return Results.Ok(security.Settings);
});

static string GetMemoryUserId(HttpContext context) =>
    context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
    ?? context.User.FindFirst("sub")?.Value
    ?? "default";

api.MapGet("/sandboxes/capabilities", (Gram.Blazor.Shared.IPlatformSandboxController controller, Gram.Blazor.Shared.ISandboxAccessPolicy policy) => Results.Ok(new
{
    availableEngines = new[] { "NativeWorktree" },
    enabled = policy.Current.Enabled,
    disabledReason = policy.Current.DisabledReason,
    hardResourceLimits = controller.Capabilities.HardResourceLimitsAvailable,
    networkIsolation = false,
    enforcementMode = controller.Capabilities.EnforcementMode
}));

api.MapPost("/sandboxes/policy", (HttpRequest http, Gram.Blazor.Shared.SandboxPolicyUpdateRequest request, Gram.Blazor.Shared.ISandboxAccessPolicy policy) =>
{
    if (!policy.IsAdminKeyValid(http.Headers["X-GramBox-Admin-Key"].FirstOrDefault())) return Results.Unauthorized();
    policy.Set(request.Enabled, request.DisabledReason);
    return Results.Ok(policy.Current);
});

api.MapPost("/sandboxes", async (Gram.Blazor.Shared.GramBoxCreateRequest request, Gram.Blazor.Shared.IGramBoxService gramBox, Gram.Blazor.Shared.ISandboxAccessPolicy policy, CancellationToken ct) =>
{
    if (!policy.CanCreate(out var policyReason))
        return Results.Json(new Gram.Blazor.Shared.ApiError("sandbox_disabled", policyReason ?? "Sandbox creation is currently disabled."), statusCode: StatusCodes.Status403Forbidden);
    if (string.IsNullOrWhiteSpace(request.RepositoryPath))
        return Results.BadRequest(new Gram.Blazor.Shared.ApiError("repository_required", "A repository path is required."));
    if (!Path.IsPathRooted(request.RepositoryPath) || !Directory.Exists(request.RepositoryPath))
        return Results.BadRequest(new Gram.Blazor.Shared.ApiError("repository_invalid", "RepositoryPath must be an existing absolute directory."));
    if (string.Equals(request.Engine, "OciContainer", StringComparison.OrdinalIgnoreCase))
        return Results.StatusCode(StatusCodes.Status501NotImplemented);

    try
    {
        var session = await gramBox.CreateSandboxAsync(request.RepositoryPath, taskName: request.Name, ct: ct, runId: request.RunId, networkMode: Gram.Blazor.Shared.GramBoxApiMapper.ParseNetworkMode(request.NetworkMode), allowedNetworkHosts: request.AllowedNetworkHosts);
        return Results.Ok(Gram.Blazor.Shared.GramBoxApiMapper.ToPublic(session));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});

api.MapPost("/sandbox-jobs", async (Gram.Blazor.Shared.SandboxJobRequest request, Gram.Blazor.Shared.ISandboxAccessPolicy policy, Gram.Blazor.Shared.ISandboxJobQueue queue, CancellationToken ct) =>
{
    if (!policy.CanCreate(out var reason)) return Results.Json(new Gram.Blazor.Shared.ApiError("sandbox_disabled", reason ?? "Sandbox creation is currently disabled."), statusCode: StatusCodes.Status403Forbidden);
    if (string.IsNullOrWhiteSpace(request.RepositoryPath) || !Path.IsPathRooted(request.RepositoryPath) || !Directory.Exists(request.RepositoryPath))
        return Results.BadRequest(new Gram.Blazor.Shared.ApiError("repository_invalid", "RepositoryPath must be an existing absolute directory."));
    if (string.Equals(request.Engine, "OciContainer", StringComparison.OrdinalIgnoreCase)) return Results.StatusCode(StatusCodes.Status501NotImplemented);
    var job = await queue.EnqueueAsync(request, ct);
    return Results.Accepted($"/api/v1/sandbox-jobs/{job.JobId}", job);
});

api.MapGet("/sandbox-jobs/{id}", async (string id, Gram.Blazor.Shared.ISandboxJobQueue queue, CancellationToken ct) => await queue.GetAsync(id, ct) is { } job ? Results.Ok(job) : Results.NotFound(new Gram.Blazor.Shared.ApiError("job_not_found", "Sandbox job was not found.")));
api.MapPost("/sandbox-jobs/{id}/cancel", (string id, Gram.Blazor.Shared.ISandboxJobQueue queue) => queue.Cancel(id) ? Results.Accepted() : Results.NotFound(new Gram.Blazor.Shared.ApiError("job_not_found_or_finished", "Sandbox job was not found or has already finished.")));
api.MapGet("/sandbox-jobs/{id}/events", async (string id, HttpContext context, Gram.Blazor.Shared.ISandboxJobQueue queue, CancellationToken ct) =>
{
    if (queue.Get(id) is null) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    await foreach (var update in queue.SubscribeAsync(id, ct))
    {
        await context.Response.WriteAsync($"event: sandbox.job\ndata: {JsonSerializer.Serialize(update, Gram.Blazor.Shared.BackendJson.Options)}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }
});

api.MapPost("/sandboxes/{id}/commands", async (string id, Gram.Blazor.Shared.GramBoxCommandRequest request, Gram.Blazor.Shared.IGramBoxService gramBox, CancellationToken ct) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.Command)) return Results.BadRequest(new Gram.Blazor.Shared.ApiError("command_required", "A command is required."));
        var result = await gramBox.ExecuteAsync(request.Command, id, request.WorkingDirectory, Math.Clamp(request.TimeoutSeconds, 1, 180), ct);
        return Results.Ok(new
        {
            success = result.Success,
            exitCode = result.ExitCode,
            stdout = result.Stdout,
            stderr = result.Stderr,
            elapsedMilliseconds = result.ElapsedMs,
            error = result.ErrorMessage,
            peakWorkingSetBytes = result.PeakWorkingSetBytes,
            cpuTimeMilliseconds = result.CpuTimeMs,
            outputBytes = result.OutputBytes
        });
    }
    catch (KeyNotFoundException) { return Results.NotFound(new Gram.Blazor.Shared.ApiError("sandbox_not_found", "Sandbox was not found.")); }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError); }
});

api.MapPost("/sandboxes/{id}/files", async (string id, Gram.Blazor.Shared.GramBoxFileWriteRequest request, Gram.Blazor.Shared.IGramBoxService gramBox, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.RelativePath)) return Results.BadRequest(new Gram.Blazor.Shared.ApiError("path_required", "A relative file path is required."));
    try { return Results.Ok(await gramBox.WriteFileAsync(request.RelativePath, request.Content ?? "", id, ct)); }
    catch (KeyNotFoundException) { return Results.NotFound(new Gram.Blazor.Shared.ApiError("sandbox_not_found", "Sandbox was not found.")); }
    catch (Exception ex) { return Results.BadRequest(new Gram.Blazor.Shared.ApiError("file_write_failed", ex.Message)); }
});

api.MapGet("/sandboxes/{id}/files", async (string id, HttpRequest http, Gram.Blazor.Shared.IGramBoxService gramBox, CancellationToken ct) =>
{
    var relativePath = http.Query["path"].ToString();
    if (string.IsNullOrWhiteSpace(relativePath)) return Results.BadRequest(new Gram.Blazor.Shared.ApiError("path_required", "A relative file path is required."));
    try
    {
        var content = await gramBox.ReadFileAsync(relativePath, id, ct);
        return content is null ? Results.NotFound(new Gram.Blazor.Shared.ApiError("file_not_found", "File was not found.")) : Results.Ok(new { relativePath, content, found = true });
    }
    catch (KeyNotFoundException) { return Results.NotFound(new Gram.Blazor.Shared.ApiError("sandbox_not_found", "Sandbox was not found.")); }
    catch (Exception ex) { return Results.BadRequest(new Gram.Blazor.Shared.ApiError("file_read_failed", ex.Message)); }
});

api.MapGet("/sandboxes/{id}/diff", async (string id, Gram.Blazor.Shared.IGramBoxService gramBox, CancellationToken ct) =>
{
    try { return Results.Ok(await gramBox.GetDiffAsync(id, ct)); }
    catch (KeyNotFoundException) { return Results.NotFound(new Gram.Blazor.Shared.ApiError("sandbox_not_found", "Sandbox was not found.")); }
});

api.MapDelete("/sandboxes/{id}", async (string id, Gram.Blazor.Shared.IGramBoxService gramBox, CancellationToken ct) =>
    await gramBox.CloseSandboxAsync(id, force: true, ct) ? Results.NoContent() : Results.NotFound(new Gram.Blazor.Shared.ApiError("sandbox_not_found", "Sandbox was not found.")));

api.MapGet("/sandboxes/{id}/events", async (string id, HttpContext context, Gram.Blazor.Shared.IGramBoxService gramBox, CancellationToken ct) =>
{
    var session = gramBox.ActiveSessions.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    if (session is null) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    long sequence = 0;
    var lastStatus = session.Status.ToString();
    await context.Response.WriteAsync($"id: {++sequence}\nevent: sandbox.status\ndata: {JsonSerializer.Serialize(new { sequence, sandboxId = id, type = "sandbox.status", message = lastStatus, timestamp = DateTimeOffset.UtcNow })}\n\n", ct);
    await context.Response.Body.FlushAsync(ct);
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(500, ct);
        session = gramBox.ActiveSessions.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (session is null) break;
        var status = session.Status.ToString();
        if (status == lastStatus) continue;
        lastStatus = status;
        await context.Response.WriteAsync($"id: {++sequence}\nevent: sandbox.status\ndata: {JsonSerializer.Serialize(new { sequence, sandboxId = id, type = "sandbox.status", message = status, timestamp = DateTimeOffset.UtcNow })}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
        if (status is "Disposed" or "Error") break;
    }
});

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

app.MapGet("/api/attachments/{conversationId}/{fileName}", (string conversationId, string fileName, Gram.Blazor.Shared.SessionStorageService storage) =>
{
    var path = storage.GetAttachmentPath(conversationId, fileName);
    if (!File.Exists(path)) return Results.NotFound(new { error = "Attachment not found" });

    var ext = Path.GetExtension(fileName).ToLowerInvariant();
    var contentType = ext switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".csv" => "text/csv; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".txt" or ".md" => "text/plain; charset=utf-8",
        _ => "application/octet-stream"
    };

    return Results.File(path, contentType, enableRangeProcessing: true);
});

app.MapGet("/api/documents/parse/{conversationId}/{fileName}", async (string conversationId, string fileName, Gram.Blazor.Shared.SessionStorageService storage, Gram.Blazor.Shared.DocumentParserService parser) =>
{
    var path = storage.GetAttachmentPath(conversationId, fileName);
    if (!File.Exists(path)) return Results.NotFound(new { error = "Attachment not found" });

    var result = await parser.ParseDocumentAsync(path, fileName);
    return Results.Ok(result);
});

app.MapGet("/api/storage/status", async (Gram.Blazor.Shared.IObjectStorageService objectStorage) =>
{
    var connected = await objectStorage.TestConnectionAsync();
    return Results.Ok(new
    {
        provider = objectStorage.ProviderName,
        bucket = objectStorage.BucketName,
        isCloudConfigured = objectStorage.IsCloudConfigured,
        connected = connected
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
