using System.Net;
using System.Text;
using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class ReminderConnector : IConnectedConnector
{
    private readonly ITaskAndMemoryService _tasks;
    public ReminderConnector(ITaskAndMemoryService tasks) => _tasks = tasks;
    public ConnectorDescriptor Descriptor => new("reminders", "Reminders", ConnectorTransport.Native, "Create and manage Gram reminders.", false);
    public IReadOnlyList<ConnectorAction> Actions =>
    [
        new("create", "reminders", "Create reminder", "Schedule a reminder for the user.", ActionRiskLevel.Moderate, RequiresApproval: true),
        new("list", "reminders", "List reminders", "List upcoming reminders.", ActionRiskLevel.Safe),
        new("dismiss", "reminders", "Dismiss reminder", "Dismiss an existing reminder.", ActionRiskLevel.Moderate, RequiresApproval: true)
    ];

    public async Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default)
    {
        using var document = request.Parameters.ValueKind == JsonValueKind.Undefined ? JsonDocument.Parse("{}") : JsonDocument.Parse(request.Parameters.GetRawText());
        var root = document.RootElement;
        if (request.ActionId.Equals("list", StringComparison.OrdinalIgnoreCase))
            return new(true, "completed", Data: JsonSerializer.SerializeToElement(await _tasks.GetRemindersAsync(false)));
        if (request.ActionId.Equals("dismiss", StringComparison.OrdinalIgnoreCase) && root.TryGetProperty("reminderId", out var id))
            return new(await _tasks.DismissReminderAsync(id.GetString() ?? ""), "completed");
        if (request.ActionId.Equals("create", StringComparison.OrdinalIgnoreCase) && root.TryGetProperty("title", out var title) && root.TryGetProperty("remindAt", out var when) && DateTimeOffset.TryParse(when.GetString(), out var remindAt))
            return new(true, "completed", Data: JsonSerializer.SerializeToElement(await _tasks.CreateReminderAsync(title.GetString() ?? "Reminder", remindAt)));
        return new(false, "invalid_parameters", "The reminder action parameters were incomplete.");
    }
}

public sealed class ControlledHttpConnector : IConnectedConnector
{
    private readonly HttpClient _http;
    public ControlledHttpConnector(HttpClient? http = null) => _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    public ConnectorDescriptor Descriptor => new("controlled-http", "Controlled HTTP", ConnectorTransport.Native, "Make explicitly approved HTTPS requests to public endpoints.", false);
    public IReadOnlyList<ConnectorAction> Actions => [new("post", "controlled-http", "POST request", "Send a JSON POST request to a public HTTPS endpoint.", ActionRiskLevel.Sensitive, RequiresApproval: true)];

    public async Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.ActionId.Equals("post", StringComparison.OrdinalIgnoreCase)) return new(false, "action_not_found");
        using var document = JsonDocument.Parse(request.Parameters.GetRawText());
        var root = document.RootElement;
        if (!root.TryGetProperty("url", out var urlValue) || !Uri.TryCreate(urlValue.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || IsPrivateHost(uri.Host))
            return new(false, "unsafe_url", "Only public HTTPS endpoints are allowed.");
        var body = root.TryGetProperty("body", out var bodyValue) ? bodyValue.GetRawText() : "{}";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(uri, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return new(response.IsSuccessStatusCode, response.IsSuccessStatusCode ? "completed" : "provider_error", responseBody);
    }

    private static bool IsPrivateHost(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.Equals("127.0.0.1") || host.Equals("::1") || IPAddress.TryParse(host, out var ip) && (IPAddress.IsLoopback(ip) || ip.GetAddressBytes()[0] == 10 || ip.GetAddressBytes()[0] == 192 && ip.GetAddressBytes()[1] == 168 || ip.GetAddressBytes()[0] == 172 && ip.GetAddressBytes()[1] >= 16 && ip.GetAddressBytes()[1] <= 31);
}

public sealed class DesktopUseConnector : IConnectedConnector
{
    public ConnectorDescriptor Descriptor => new(
        "desktop-use",
        "Desktop Use",
        ConnectorTransport.LocalAgent,
        "Experimental desktop observation and computer-control capability.",
        RequiresConnection: true,
        IsEnabled: FeatureFlags.DesktopUseEnabled);

    public IReadOnlyList<ConnectorAction> Actions =>
    [
        new("observe_screen", "desktop-use", "Observe screen", "Capture a redacted view of the active screen.", ActionRiskLevel.Safe),
        new("inspect_accessibility_tree", "desktop-use", "Inspect accessibility tree", "Read accessible controls from the active application.", ActionRiskLevel.Safe),
        new("focus_window", "desktop-use", "Focus window", "Bring an approved application window to the foreground.", ActionRiskLevel.Moderate, RequiresApproval: true),
        new("move_pointer", "desktop-use", "Move pointer", "Move the pointer to an approved UI target.", ActionRiskLevel.Moderate, RequiresApproval: true),
        new("click", "desktop-use", "Click", "Click an approved UI target.", ActionRiskLevel.Moderate, RequiresApproval: true),
        new("scroll", "desktop-use", "Scroll", "Scroll within the active application.", ActionRiskLevel.Moderate, RequiresApproval: true),
        new("type_text", "desktop-use", "Type text", "Type non-sensitive text into an approved control.", ActionRiskLevel.Sensitive, RequiresApproval: true),
        new("press_key", "desktop-use", "Press key", "Send an approved keyboard shortcut.", ActionRiskLevel.Sensitive, RequiresApproval: true),
        new("select_menu", "desktop-use", "Select menu", "Choose an approved native menu item.", ActionRiskLevel.Sensitive, RequiresApproval: true),
        new("capture_region", "desktop-use", "Capture region", "Capture a redacted region of the active screen.", ActionRiskLevel.Safe)
    ];

    public Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(FeatureFlags.DesktopUseEnabled
            ? new ConnectorActionResult(false, "local_agent_required", "Desktop Use requires the paired desktop local agent.")
            : new ConnectorActionResult(false, "feature_disabled", "Desktop Use is reserved for a future release."));
}

public abstract class ConnectionGatedConnector : IConnectedConnector
{
    public abstract ConnectorDescriptor Descriptor { get; }
    public abstract IReadOnlyList<ConnectorAction> Actions { get; }
    public virtual Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConnectorActionResult(false, "connection_required", $"Connect {Descriptor.Name} before using this action."));
}

public sealed class SpotifyConnector : ConnectionGatedConnector
{
    private readonly IConnectorCredentialVault? _vault;
    private readonly HttpClient _http;
    public SpotifyConnector(IConnectorCredentialVault? vault = null, HttpClient? http = null) { _vault = vault; _http = http ?? new HttpClient { BaseAddress = new Uri("https://api.spotify.com/v1"), Timeout = TimeSpan.FromSeconds(20) }; }
    public override ConnectorDescriptor Descriptor => new("spotify", "Spotify", ConnectorTransport.Native, "Control playback and search the connected Spotify account.", true);
    public override IReadOnlyList<ConnectorAction> Actions =>
    [
        new("status", "spotify", "Playback status", "Read the current playback state.", ActionRiskLevel.Safe, RequiredScopes: ["user-read-playback-state"]),
        new("search", "spotify", "Search Spotify", "Search tracks, albums, artists, and playlists.", ActionRiskLevel.Safe),
        new("play", "spotify", "Start playback", "Start playback on an available device or URI.", ActionRiskLevel.Moderate, RequiresApproval: true, RequiredScopes: ["user-modify-playback-state"]),
        new("pause", "spotify", "Pause playback", "Pause current playback.", ActionRiskLevel.Moderate, RequiresApproval: true, RequiredScopes: ["user-modify-playback-state"]),
        new("next", "spotify", "Next track", "Skip to the next track.", ActionRiskLevel.Moderate, RequiresApproval: true, RequiredScopes: ["user-modify-playback-state"])
    ];

    public override async Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default)
    {
        if (_vault == null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.ConnectionId)) return new(false, "connection_required", "A verified Spotify connection is required.");
        var token = await _vault.ResolveAsync(new ConnectorCredentialReference(request.UserId, "spotify", request.ConnectionId, "access_token"), ["access_token"], cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return new(false, "reauthorization_required", "The Spotify access token is unavailable.");
        using var document = request.Parameters.ValueKind == JsonValueKind.Undefined ? JsonDocument.Parse("{}") : JsonDocument.Parse(request.Parameters.GetRawText());
        var root = document.RootElement;
        HttpRequestMessage message;
        if (request.ActionId.Equals("status", StringComparison.OrdinalIgnoreCase)) message = new(HttpMethod.Get, "/me/player");
        else if (request.ActionId.Equals("search", StringComparison.OrdinalIgnoreCase))
        {
            var query = root.TryGetProperty("query", out var q) ? q.GetString() : null;
            if (string.IsNullOrWhiteSpace(query)) return new(false, "invalid_parameters", "A Spotify search query is required.");
            message = new(HttpMethod.Get, $"/search?q={Uri.EscapeDataString(query)}&type=track,album,artist,playlist&limit=10");
        }
        else if (request.ActionId.Equals("pause", StringComparison.OrdinalIgnoreCase)) message = new(HttpMethod.Put, "/me/player/pause");
        else if (request.ActionId.Equals("next", StringComparison.OrdinalIgnoreCase)) message = new(HttpMethod.Post, "/me/player/next");
        else if (request.ActionId.Equals("play", StringComparison.OrdinalIgnoreCase))
        {
            message = new(HttpMethod.Put, "/me/player/play");
            if (root.TryGetProperty("uri", out var uri) && !string.IsNullOrWhiteSpace(uri.GetString())) message.Content = new StringContent(JsonSerializer.Serialize(new { uris = new[] { uri.GetString() } }), Encoding.UTF8, "application/json");
        }
        else return new(false, "action_not_found");
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using (message)
        using (var response = await _http.SendAsync(message, cancellationToken))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 12000) body = body[..12000] + "…";
            return new(response.IsSuccessStatusCode, response.IsSuccessStatusCode ? "completed" : response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "reauthorization_required" : "provider_error", body);
        }
    }
}

public sealed class HomeAssistantConnector : ConnectionGatedConnector
{
    public override ConnectorDescriptor Descriptor => new("home-assistant", "Home Assistant", ConnectorTransport.Native, "Inspect and control allowlisted smart-home entities.", true);
    public override IReadOnlyList<ConnectorAction> Actions =>
    [
        new("list_entities", "home-assistant", "List devices", "List exposed entities and states.", ActionRiskLevel.Safe, RequiredScopes: ["read"]),
        new("get_state", "home-assistant", "Get state", "Read an exposed entity state.", ActionRiskLevel.Safe, RequiredScopes: ["read"]),
        new("turn_on", "home-assistant", "Turn on", "Turn on an approved entity.", ActionRiskLevel.Moderate, RequiresApproval: true, RequiredScopes: ["control"]),
        new("turn_off", "home-assistant", "Turn off", "Turn off an approved entity.", ActionRiskLevel.Moderate, RequiresApproval: true, RequiredScopes: ["control"]),
        new("activate_scene", "home-assistant", "Activate scene", "Activate an approved scene.", ActionRiskLevel.Moderate, RequiresApproval: true, RequiredScopes: ["control"])
    ];
}

public sealed class PostmanConnector : ConnectionGatedConnector
{
    public override ConnectorDescriptor Descriptor => new("postman", "Postman", ConnectorTransport.Native, "Manage Postman workspaces, collections, environments, and monitors.", true);
    public override IReadOnlyList<ConnectorAction> Actions =>
    [
        new("list_collections", "postman", "List collections", "List collections available to the connected account.", ActionRiskLevel.Safe, RequiredScopes: ["collections:read"]),
        new("create_collection", "postman", "Create collection", "Create a collection in a selected workspace.", ActionRiskLevel.Moderate, RequiresApproval: true, RequiredScopes: ["collections:write"]),
        new("update_environment", "postman", "Update environment", "Update an environment value without exposing secrets to the model.", ActionRiskLevel.Sensitive, RequiresApproval: true, RequiredScopes: ["environments:write"]),
        new("run_monitor", "postman", "Run monitor", "Trigger an existing Postman monitor.", ActionRiskLevel.Moderate, RequiresApproval: true, RequiredScopes: ["monitors:write"])
    ];
}

public sealed class McpBridgeConnector : ConnectionGatedConnector
{
    public override ConnectorDescriptor Descriptor => new("mcp", "MCP", ConnectorTransport.Mcp, "Use registered Model Context Protocol servers through Gram's policy engine.", true);
    public override IReadOnlyList<ConnectorAction> Actions =>
    [
        new("list_tools", "mcp", "List MCP tools", "List tools exposed by an approved MCP server.", ActionRiskLevel.Safe),
        new("invoke_tool", "mcp", "Invoke MCP tool", "Invoke an approved MCP tool after schema and risk validation.", ActionRiskLevel.Moderate, RequiresApproval: true)
    ];
}
