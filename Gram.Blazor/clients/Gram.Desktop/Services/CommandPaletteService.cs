namespace Gram.Desktop.Services;

public enum CommandPaletteCategory
{
    All,
    Command,
    Tab,
    Chat,
    File
}

public sealed class CommandPaletteItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public CommandPaletteCategory Category { get; init; } = CommandPaletteCategory.Command;
    public string? Shortcut { get; init; }
    public string? IconSvg { get; init; }
    public Action? Action { get; init; }
    public Func<Task>? ActionAsync { get; init; }
}

public sealed class CommandPaletteService
{
    public bool IsOpen { get; private set; }
    public string Query { get; private set; } = "";
    public CommandPaletteCategory ActiveCategory { get; private set; } = CommandPaletteCategory.All;
    public int SelectedIndex { get; private set; } = 0;

    public event Action? OnChange;

    public void Open(string initialQuery = "")
    {
        IsOpen = true;
        Query = initialQuery;
        SelectedIndex = 0;
        OnChange?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        Query = "";
        SelectedIndex = 0;
        OnChange?.Invoke();
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void SetQuery(string query)
    {
        Query = query;
        SelectedIndex = 0;
        OnChange?.Invoke();
    }

    public void SetCategory(CommandPaletteCategory category)
    {
        ActiveCategory = category;
        SelectedIndex = 0;
        OnChange?.Invoke();
    }

    public void SetSelectedIndex(int index)
    {
        SelectedIndex = index;
        OnChange?.Invoke();
    }
}
