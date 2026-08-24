using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class WorkspaceTabService
{
    private readonly StorageService _storageService;
    private readonly string _tabsFilePath;

    public List<WorkspaceTab> OpenTabs { get; private set; } = new();
    public string ActiveTabId { get; private set; } = "";
    public WorkspaceTab? ActiveTab => OpenTabs.FirstOrDefault(t => t.Id == ActiveTabId);

    public event Action? OnTabsChanged;

    public WorkspaceTabService(StorageService storageService)
    {
        _storageService = storageService;
        _tabsFilePath = _storageService.GetFilePath("open_tabs.json");
        LoadTabs();
    }

    public void LoadTabs()
    {
        try
        {
            if (File.Exists(_tabsFilePath))
            {
                var json = File.ReadAllText(_tabsFilePath);
                var loaded = JsonSerializer.Deserialize<List<WorkspaceTab>>(json);
                if (loaded != null && loaded.Count > 0)
                {
                    OpenTabs = loaded;
                    ActiveTabId = OpenTabs[0].Id;
                    return;
                }
            }
        }
        catch {}

        // Default initial tab
        var defaultTab = new WorkspaceTab
        {
            Id = "tab-chat-default",
            Title = "New chat",
            Type = WorkspaceTabType.Chat,
            ChatId = "default",
            Icon = null
        };
        OpenTabs = [defaultTab];
        ActiveTabId = defaultTab.Id;
    }

    public void SaveTabs()
    {
        try
        {
            var json = JsonSerializer.Serialize(OpenTabs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_tabsFilePath, json);
        }
        catch {}
        OnTabsChanged?.Invoke();
    }

    public void SelectTab(string tabId)
    {
        if (OpenTabs.Any(t => t.Id == tabId))
        {
            ActiveTabId = tabId;
            OnTabsChanged?.Invoke();
        }
    }

    public void OpenChatTab(string chatId, string chatTitle)
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == WorkspaceTabType.Chat && t.ChatId == chatId);
        if (existing != null)
        {
            if (!string.IsNullOrWhiteSpace(chatTitle) && chatTitle != "New chat")
            {
                existing.Title = chatTitle;
            }
            ActiveTabId = existing.Id;
        }
        else
        {
            var newTab = new WorkspaceTab
            {
                Title = string.IsNullOrWhiteSpace(chatTitle) ? "New chat" : chatTitle,
                Type = WorkspaceTabType.Chat,
                ChatId = chatId,
                Icon = null
            };
            OpenTabs.Add(newTab);
            ActiveTabId = newTab.Id;
        }
        SaveTabs();
    }

    public void OpenToolboxTab()
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == WorkspaceTabType.ToolboxOverview);
        if (existing != null)
        {
            ActiveTabId = existing.Id;
        }
        else
        {
            var newTab = new WorkspaceTab
            {
                Title = "Toolbox",
                Type = WorkspaceTabType.ToolboxOverview,
                Icon = null
            };
            OpenTabs.Add(newTab);
            ActiveTabId = newTab.Id;
        }
        SaveTabs();
    }

    public void OpenToolConfigTab(string toolId, string toolName, string? icon = null)
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == WorkspaceTabType.ToolConfig && t.ToolId == toolId);
        if (existing != null)
        {
            ActiveTabId = existing.Id;
        }
        else
        {
            var newTab = new WorkspaceTab
            {
                Title = toolName,
                Type = WorkspaceTabType.ToolConfig,
                ToolId = toolId,
                Icon = null
            };
            OpenTabs.Add(newTab);
            ActiveTabId = newTab.Id;
        }
        SaveTabs();
    }

    public void CloseTab(string tabId)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return;

        var index = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);

        if (OpenTabs.Count == 0)
        {
            var defaultTab = new WorkspaceTab
            {
                Title = "New chat",
                Type = WorkspaceTabType.Chat,
                ChatId = "default",
                Icon = null
            };
            OpenTabs.Add(defaultTab);
            ActiveTabId = defaultTab.Id;
        }
        else if (ActiveTabId == tabId)
        {
            var newIndex = Math.Min(index, OpenTabs.Count - 1);
            ActiveTabId = OpenTabs[newIndex].Id;
        }

        SaveTabs();
    }

    public void UpdateChatTabTitle(string chatId, string newTitle)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Type == WorkspaceTabType.Chat && t.ChatId == chatId);
        if (tab != null && tab.Title != newTitle)
        {
            tab.Title = newTitle;
            SaveTabs();
        }
    }
}
