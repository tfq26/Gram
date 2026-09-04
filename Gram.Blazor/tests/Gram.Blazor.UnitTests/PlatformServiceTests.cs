using Gram.Desktop.Services;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class PlatformServiceTests
{
    [Fact]
    public void MacOS_PlatformAttributes_ReturnMacNativeConventions()
    {
        var platform = new PlatformService();
        platform.SetPlatformOverride(OSPlatformType.MacOS);

        Assert.Equal(OSPlatformType.MacOS, platform.CurrentPlatform);
        Assert.True(platform.IsMacOS);
        Assert.False(platform.IsWindows);
        Assert.Equal("os-macos", platform.PlatformCssClass);
        Assert.Equal("⌘", platform.ModifierKeyLabel);
        Assert.Equal("⌘↵", platform.SubmitShortcutLabel);

        // macOS submit triggers on MetaKey (Command), not CtrlKey
        Assert.True(platform.IsSubmitKeyPressed(ctrlKey: false, metaKey: true));
        Assert.False(platform.IsSubmitKeyPressed(ctrlKey: true, metaKey: false));
    }

    [Fact]
    public void Windows_PlatformAttributes_ReturnWindowsFluentConventions()
    {
        var platform = new PlatformService();
        platform.SetPlatformOverride(OSPlatformType.Windows);

        Assert.Equal(OSPlatformType.Windows, platform.CurrentPlatform);
        Assert.True(platform.IsWindows);
        Assert.False(platform.IsMacOS);
        Assert.Equal("os-windows", platform.PlatformCssClass);
        Assert.Equal("Ctrl", platform.ModifierKeyLabel);
        Assert.Equal("Ctrl+↵", platform.SubmitShortcutLabel);

        // Windows submit triggers on CtrlKey, not MetaKey
        Assert.True(platform.IsSubmitKeyPressed(ctrlKey: true, metaKey: false));
        Assert.False(platform.IsSubmitKeyPressed(ctrlKey: false, metaKey: true));
    }

    [Fact]
    public void Linux_PlatformAttributes_ReturnAdwaitaConventions()
    {
        var platform = new PlatformService();
        platform.SetPlatformOverride(OSPlatformType.Linux);

        Assert.Equal(OSPlatformType.Linux, platform.CurrentPlatform);
        Assert.True(platform.IsLinux);
        Assert.Equal("os-linux", platform.PlatformCssClass);
        Assert.Equal("Ctrl", platform.ModifierKeyLabel);
        Assert.True(platform.IsSubmitKeyPressed(ctrlKey: true, metaKey: false));
    }
}
