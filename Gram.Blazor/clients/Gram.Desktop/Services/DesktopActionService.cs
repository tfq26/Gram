namespace Gram.Desktop.Services;

public sealed class DesktopActionService
{
    public event Action? OnNewConversationRequested;
    public event Action? OnSettingsRequested;
    public event Action? OnFocusRequested;
    public event Action? OnToggleSidebarRequested;

    public void RequestNewConversation() => OnNewConversationRequested?.Invoke();
    public void RequestSettings() => OnSettingsRequested?.Invoke();
    public void RequestFocus() => OnFocusRequested?.Invoke();
    public void RequestToggleSidebar() => OnToggleSidebarRequested?.Invoke();
}
