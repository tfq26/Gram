using Microsoft.AspNetCore.Components;

namespace Gram.Desktop.Services;

public sealed record TopBarAction(
    string Id,
    string Label,
    string? Tooltip = null,
    string? IconSvg = null,
    string? Shortcut = null,
    Action? OnClick = null,
    Func<Task>? OnClickAsync = null,
    bool IsActive = false,
    string? Badge = null
);

public sealed class TopBarService
{
    private readonly object _lock = new();
    private List<TopBarAction> _contextActions = new();
    private RenderFragment? _centerWidget;
    private string? _ownerId;

    public event Action? OnChanged;

    public IReadOnlyList<TopBarAction> ContextActions
    {
        get
        {
            lock (_lock) { return _contextActions.ToList(); }
        }
    }

    public RenderFragment? CenterWidget
    {
        get
        {
            lock (_lock) { return _centerWidget; }
        }
    }

    public void SetContext(string ownerId, IEnumerable<TopBarAction> actions, RenderFragment? centerWidget = null)
    {
        lock (_lock)
        {
            _ownerId = ownerId;
            _contextActions = actions.ToList();
            _centerWidget = centerWidget;
        }
        OnChanged?.Invoke();
    }

    public void ClearContext(string? ownerId = null)
    {
        lock (_lock)
        {
            if (ownerId == null || _ownerId == ownerId)
            {
                _ownerId = null;
                _contextActions.Clear();
                _centerWidget = null;
            }
        }
        OnChanged?.Invoke();
    }
}
