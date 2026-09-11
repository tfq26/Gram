using System.Net.Http.Headers;
using System.Text.Json;
using Gram.Blazor.Shared;
using Gram.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;

namespace Gram.Desktop;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var backendUrl = GetOption(args, "--backend") ?? Environment.GetEnvironmentVariable("GRAM_BACKEND_URL") ?? "http://localhost:7337";
        var token = GetOption(args, "--token") ?? Environment.GetEnvironmentVariable("GRAM_ACCESS_TOKEN");
        var message = GetOption(args, "--message");

        // If --message is provided, run headless CLI mode
        if (!string.IsNullOrWhiteSpace(message))
        {
            return await RunCliAsync(args, backendUrl, token, message);
        }

        // Native Desktop GUI Mode
        try
        {
            var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

            var http = CreateHttpClient(backendUrl);
            if (!string.IsNullOrWhiteSpace(token))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var backendClient = new GramBackendClient(http, token);
            var platformService = new PlatformService();
            var storageService = new StorageService();
            var tabService = new WorkspaceTabService(storageService);
            var session = new DesktopSession(backendClient, backendUrl, tabService: tabService);
            var modelService = new ModelService(storageService);
            var toolboxService = new ToolboxManagerService(storageService);
            var gitService = new GitService(storageService, http);
            var themeService = new ThemeService();
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            var settingsService = new SettingsService(config, storageService);
            var layoutService = new DesktopLayoutService(storageService);
            var topBarService = new TopBarService();
            var companionTaskService = new CompanionTaskService();
            var actionService = new DesktopActionService();

            appBuilder.Services.AddSingleton(http);
            appBuilder.Services.AddSingleton(backendClient);
            appBuilder.Services.AddSingleton(session);
            appBuilder.Services.AddSingleton<IPlatformService>(platformService);
            appBuilder.Services.AddSingleton<MarkdownService>();
            appBuilder.Services.AddSingleton(storageService);
            appBuilder.Services.AddSingleton(tabService);
            appBuilder.Services.AddSingleton(modelService);
            appBuilder.Services.AddSingleton(toolboxService);
            appBuilder.Services.AddSingleton(gitService);
            appBuilder.Services.AddSingleton(themeService);
            appBuilder.Services.AddSingleton(settingsService);
            appBuilder.Services.AddSingleton(layoutService);
            appBuilder.Services.AddSingleton(topBarService);
            appBuilder.Services.AddSingleton(companionTaskService);
            appBuilder.Services.AddSingleton(actionService);
            appBuilder.Services.AddSingleton<PtyTerminalService>();
            appBuilder.Services.AddSingleton<WorkspaceFileService>();
            appBuilder.Services.AddSingleton<ISonnerService, SonnerService>();
            appBuilder.Services.AddSingleton<GitGramService>();
            appBuilder.Services.AddSingleton<CommandPaletteService>();

            // Photino selectors are supplied without the CSS '#' prefix.
            appBuilder.RootComponents.Add<App>("app");

            var app = appBuilder.Build();

            var savedLayout = layoutService.GetLayout();
            var windowWidth = Math.Max(920, savedLayout.WindowWidth);
            var windowHeight = Math.Max(560, savedLayout.WindowHeight);

            app.MainWindow
                .SetTitle("Gram")
                .SetUseOsDefaultSize(false)
                .SetSize(windowWidth, windowHeight)
                .SetMinSize(920, 560);

            if (savedLayout.WindowX.HasValue && savedLayout.WindowY.HasValue)
            {
                try
                {
                    app.MainWindow.SetLocation(new System.Drawing.Point(savedLayout.WindowX.Value, savedLayout.WindowY.Value));
                }
                catch
                {
                    app.MainWindow.Center();
                }
            }
            else
            {
                app.MainWindow.Center();
            }

            // Track window mutations to guarantee crash-safe session restore
            app.MainWindow.WindowSizeChanged += (sender, size) =>
            {
                var loc = app.MainWindow.Location;
                layoutService.SaveBounds(size.Width, size.Height, loc.X, loc.Y);
            };

            app.MainWindow.WindowLocationChanged += (sender, location) =>
            {
                var size = app.MainWindow.Size;
                layoutService.SaveBounds(size.Width, size.Height, location.X, location.Y);
            };

            app.MainWindow.WindowClosing += (sender, e) =>
            {
                try
                {
                    var size = app.MainWindow.Size;
                    var loc = app.MainWindow.Location;
                    layoutService.SaveBounds(size.Width, size.Height, loc.X, loc.Y);
                }
                catch {}
                return false; // allow close
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
            {
                Console.Error.WriteLine($"[Gram Desktop Error] {error.ExceptionObject}");
            };

            var iconPath = ResolveAppIcon();
            if (!string.IsNullOrEmpty(iconPath))
            {
                try
                {
                    app.MainWindow.SetIconFile(iconPath);
                }
                catch {}
            }

            if (OperatingSystem.IsMacOS())
            {
                MacOsIntegration.Initialize(actionService, iconPath);
            }

            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to start Gram Desktop window: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunCliAsync(string[] args, string backendUrl, string? token, string message)
    {
        using var http = CreateHttpClient(backendUrl);
        if (!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var client = new GramBackendClient(http, token);
        Console.WriteLine($"Gram Desktop CLI · {http.BaseAddress}");

        try
        {
            var conversationId = GetOption(args, "--conversation");
            var workspace = GetOption(args, "--workspace");
            ConversationDetail conversation;
            if (string.IsNullOrWhiteSpace(conversationId))
                conversation = await client.PostAsync<ConversationDetail>("/api/v1/conversations", new CreateConversationRequest(ModelId: GetOption(args, "--model")));
            else
                conversation = await client.GetAsync<ConversationDetail>($"/api/v1/conversations/{Uri.EscapeDataString(conversationId)}")
                    ?? throw new InvalidOperationException("Conversation was not found.");

            var run = await client.PostAsync<RunSummary>(
                $"/api/v1/conversations/{Uri.EscapeDataString(conversation.Id)}/runs",
                new SendMessageRequest(message, GetOption(args, "--model"), WorkspacePath: workspace ?? Environment.CurrentDirectory, AllowLocalCapabilities: IsLocalBackend(backendUrl)));
            Console.WriteLine($"Run {run.Id} started with {run.ModelId}");

            await foreach (var evt in client.SubscribeToRunAsync(run.Id))
            {
                var delta = ExtractContentDelta(evt);
                if (!string.IsNullOrEmpty(delta)) Console.Write(delta);
                else if (evt.EventType is "run.completed" or "run.failed" or "run.cancelled")
                    Console.WriteLine($"\n[{evt.EventType}]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Desktop CLI error: {ex.Message}");
            return 1;
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static HttpClient CreateHttpClient(string backendUrl)
    {
        // Desktop auth uses bearer tokens; avoid cookie initialization and keep
        // the client boundary explicit for the native shell.
        var handler = new SocketsHttpHandler { UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.All };
        return new HttpClient(handler) { BaseAddress = new Uri(backendUrl.TrimEnd('/') + "/") };
    }

    private static bool IsLocalBackend(string backendUrl) => Uri.TryCreate(backendUrl, UriKind.Absolute, out var uri)
        && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase));

    private static string? ExtractContentDelta(RunEvent item)
    {
        if (item.EventType.Equals("text.delta", StringComparison.OrdinalIgnoreCase)) return item.Payload;
        if (!item.EventType.Equals("run.step", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var document = JsonDocument.Parse(item.Payload);
            return document.RootElement.TryGetProperty("contentDelta", out var value) ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? ResolveAppIcon()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon.icns"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon.ico"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon.png"),
            Path.Combine(AppContext.BaseDirectory, "..", "Resources", "AppIcon.icns"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "icons", "gram-lines.svg"),
            Path.Combine(Directory.GetCurrentDirectory(), "Gram.Blazor", "clients", "Gram.Desktop", "Resources", "AppIcon.icns"),
            Path.Combine(Directory.GetCurrentDirectory(), "Gram.Blazor", "clients", "Gram.Desktop", "Resources", "AppIcon.ico"),
            Path.Combine(Directory.GetCurrentDirectory(), "Gram.Blazor", "clients", "Gram.Desktop", "Resources", "AppIcon.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", "AppIcon.icns")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
