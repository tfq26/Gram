namespace Gram.Blazor.Shared;

public enum SonnerToastVariant
{
    Default,
    Destructive,
    Success,
    Info,
    Warning
}

public enum SonnerPosition
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
    TopCenter,
    BottomCenter
}

public sealed class SonnerToast
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public SonnerToastVariant Variant { get; set; } = SonnerToastVariant.Default;
    public int DurationMs { get; set; } = 4000;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public string? ActionText { get; set; }
    public Action? OnAction { get; set; }
}

public interface ISonnerService
{
    event Action? OnChange;
    IReadOnlyList<SonnerToast> Toasts { get; }
    void Show(string title, string? description = null, SonnerToastVariant variant = SonnerToastVariant.Default, int durationMs = 4000);
    void Success(string title, string? description = null, int durationMs = 4000);
    void Error(string title, string? description = null, int durationMs = 5000);
    void Info(string title, string? description = null, int durationMs = 4000);
    void Warning(string title, string? description = null, int durationMs = 4000);
    void Dismiss(string id);
}

public sealed class SonnerService : ISonnerService
{
    private readonly List<SonnerToast> _toasts = new();
    public event Action? OnChange;

    public IReadOnlyList<SonnerToast> Toasts => _toasts.AsReadOnly();

    public void Show(string title, string? description = null, SonnerToastVariant variant = SonnerToastVariant.Default, int durationMs = 4000)
    {
        var toast = new SonnerToast
        {
            Title = title,
            Description = description,
            Variant = variant,
            DurationMs = durationMs
        };

        _toasts.Add(toast);
        OnChange?.Invoke();

        if (durationMs > 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(durationMs);
                Dismiss(toast.Id);
            });
        }
    }

    public void Success(string title, string? description = null, int durationMs = 4000) =>
        Show(title, description, SonnerToastVariant.Success, durationMs);

    public void Error(string title, string? description = null, int durationMs = 5000) =>
        Show(title, description, SonnerToastVariant.Destructive, durationMs);

    public void Info(string title, string? description = null, int durationMs = 4000) =>
        Show(title, description, SonnerToastVariant.Info, durationMs);

    public void Warning(string title, string? description = null, int durationMs = 4000) =>
        Show(title, description, SonnerToastVariant.Warning, durationMs);

    public void Dismiss(string id)
    {
        var removed = _toasts.RemoveAll(t => t.Id == id);
        if (removed > 0)
        {
            OnChange?.Invoke();
        }
    }
}
