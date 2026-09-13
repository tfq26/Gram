using System.Net.Http.Headers;
using Gram.Blazor.Shared;
using Gram.Desktop;
using Gram.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace Gram.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var backendUrl = Environment.GetEnvironmentVariable("GRAM_BACKEND_URL") ?? "http://127.0.0.1:7337";
        var token = Environment.GetEnvironmentVariable("GRAM_ACCESS_TOKEN");

        var http = CreateHttpClient(backendUrl);
        if (!string.IsNullOrWhiteSpace(token))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

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

        builder.Services.AddSingleton(http);
        builder.Services.AddSingleton(backendClient);
        builder.Services.AddSingleton(session);
        builder.Services.AddSingleton<IPlatformService>(platformService);
        builder.Services.AddSingleton<MarkdownService>();
        builder.Services.AddSingleton(storageService);
        builder.Services.AddSingleton(tabService);
        builder.Services.AddSingleton(modelService);
        builder.Services.AddSingleton(toolboxService);
        builder.Services.AddSingleton(gitService);
        builder.Services.AddSingleton(themeService);
        builder.Services.AddSingleton(settingsService);
        builder.Services.AddSingleton(layoutService);
        builder.Services.AddSingleton(topBarService);
        builder.Services.AddSingleton(companionTaskService);
        builder.Services.AddSingleton(actionService);
        builder.Services.AddSingleton<PtyTerminalService>();
        builder.Services.AddSingleton<WorkspaceFileService>();
        builder.Services.AddSingleton<ISonnerService, SonnerService>();
        builder.Services.AddSingleton<GitGramService>();
        builder.Services.AddSingleton<CommandPaletteService>();

        return builder.Build();
    }

    private static HttpClient CreateHttpClient(string backendUrl)
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        return new HttpClient(handler) { BaseAddress = new Uri(backendUrl.TrimEnd('/') + "/") };
    }
}
