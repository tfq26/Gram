using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.JSInterop;

namespace Gram.Desktop.Services;

public enum OSPlatformType
{
    MacOS,
    Windows,
    Linux,
    Unknown
}

public interface IPlatformService
{
    OSPlatformType CurrentPlatform { get; }
    bool IsMacOS { get; }
    bool IsWindows { get; }
    bool IsLinux { get; }
    string PlatformCssClass { get; }
    string PlatformName { get; }
    string ModifierKeyLabel { get; }
    string SubmitShortcutLabel { get; }
    bool IsSubmitKeyPressed(bool ctrlKey, bool metaKey);
    void SetPlatformOverride(OSPlatformType? platform);
    void OpenExternalUrl(string url);
}

public sealed class PlatformService : IPlatformService
{
    private OSPlatformType? _overridePlatform;

    public OSPlatformType CurrentPlatform
    {
        get
        {
            if (_overridePlatform.HasValue) return _overridePlatform.Value;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return OSPlatformType.MacOS;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return OSPlatformType.Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return OSPlatformType.Linux;
            return OSPlatformType.Unknown;
        }
    }

    public bool IsMacOS => CurrentPlatform == OSPlatformType.MacOS;
    public bool IsWindows => CurrentPlatform == OSPlatformType.Windows;
    public bool IsLinux => CurrentPlatform == OSPlatformType.Linux;

    public string PlatformCssClass => CurrentPlatform switch
    {
        OSPlatformType.MacOS => "os-macos",
        OSPlatformType.Windows => "os-windows",
        OSPlatformType.Linux => "os-linux",
        _ => "os-generic"
    };

    public string PlatformName => CurrentPlatform switch
    {
        OSPlatformType.MacOS => "macOS",
        OSPlatformType.Windows => "Windows",
        OSPlatformType.Linux => "Linux",
        _ => "Desktop"
    };

    public string ModifierKeyLabel => IsMacOS ? "⌘" : "Ctrl";

    public string SubmitShortcutLabel => IsMacOS ? "⌘↵" : "Ctrl+↵";

    public bool IsSubmitKeyPressed(bool ctrlKey, bool metaKey)
    {
        return IsMacOS ? metaKey : ctrlKey;
    }

    public void SetPlatformOverride(OSPlatformType? platform)
    {
        _overridePlatform = platform;
    }

    [JSInvokable("OpenExternalUrl")]
    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != "mailto" && uri.Scheme != Uri.UriSchemeFile)
                return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", uri.AbsoluteUri);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", uri.AbsoluteUri);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PlatformService] Failed to open external URL '{url}': {ex.Message}");
        }
    }

    public void OpenExternalUrl(string url) => OpenUrl(url);
}
