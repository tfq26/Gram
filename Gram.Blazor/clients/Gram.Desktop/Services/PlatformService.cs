using System.Runtime.InteropServices;

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
}
