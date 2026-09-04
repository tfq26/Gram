using System.Text.Json;
using Gram.Blazor.Shared;

namespace Gram.Desktop.Services;

public sealed class DesktopLayoutState
{
    public int WindowWidth { get; set; } = 1240;
    public int WindowHeight { get; set; } = 800;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool IsMaximized { get; set; } = false;
    public bool IsSidebarOpen { get; set; } = true;
    public bool IsInfosheetOpen { get; set; } = true;
    public string? SelectedModelId { get; set; }
}

public sealed class DesktopLayoutService
{
    private readonly StorageService _storageService;
    private readonly string _filePath;
    private readonly object _lock = new();
    private DesktopLayoutState _state = new();

    public DesktopLayoutService(StorageService storageService)
    {
        _storageService = storageService;
        _filePath = _storageService.GetFilePath("desktop_layout.json");
        Load();
    }

    public DesktopLayoutState GetLayout()
    {
        lock (_lock)
        {
            return _state;
        }
    }

    public void SaveBounds(int width, int height, int? x, int? y, bool isMaximized = false)
    {
        lock (_lock)
        {
            // Enforce minimum reasonable dimensions
            if (width >= 600) _state.WindowWidth = width;
            if (height >= 400) _state.WindowHeight = height;
            if (x.HasValue && Math.Abs(x.Value) < 10000) _state.WindowX = x.Value;
            if (y.HasValue && Math.Abs(y.Value) < 10000) _state.WindowY = y.Value;
            _state.IsMaximized = isMaximized;
            Persist();
        }
    }

    public void SavePanes(bool isSidebarOpen, bool isInfosheetOpen)
    {
        lock (_lock)
        {
            _state.IsSidebarOpen = isSidebarOpen;
            _state.IsInfosheetOpen = isInfosheetOpen;
            Persist();
        }
    }

    public void SaveSelectedModel(string? modelId)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(modelId))
            {
                _state.SelectedModelId = modelId;
                Persist();
            }
        }
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var loaded = JsonSerializer.Deserialize<DesktopLayoutState>(json);
                    if (loaded != null)
                    {
                        // Validate minimum size
                        if (loaded.WindowWidth < 920) loaded.WindowWidth = 1240;
                        if (loaded.WindowHeight < 560) loaded.WindowHeight = 800;
                        _state = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DesktopLayoutService] Failed to load layout: {ex.Message}");
            }
        }
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            var tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DesktopLayoutService] Failed to persist layout: {ex.Message}");
        }
    }
}
