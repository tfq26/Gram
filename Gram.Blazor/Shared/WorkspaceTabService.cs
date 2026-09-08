using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class WorkspaceTabSessionState
{
    public List<WorkspaceTab> OpenTabs { get; set; } = new();
    public string ActiveTabId { get; set; } = "";
    public bool IsSplitView { get; set; }
    public string? SecondaryTabId { get; set; }
}

public sealed class WorkspaceTabService
{
    private readonly StorageService _storageService;
    private readonly string _tabsFilePath;

    public List<WorkspaceTab> OpenTabs { get; private set; } = new();
    public string ActiveTabId { get; private set; } = "";
    public WorkspaceTab? ActiveTab => OpenTabs.FirstOrDefault(t => t.Id == ActiveTabId);

    public bool IsSplitView { get; private set; }
    public string? SecondaryTabId { get; private set; }
    public WorkspaceTab? SecondaryTab => OpenTabs.FirstOrDefault(t => t.Id == SecondaryTabId);

    public bool IsDraggingTab { get; private set; }
    public string? DraggedTabId { get; private set; }

    public event Action? OnTabsChanged;
    public event Action? OnDragStateChanged;
    public event Action<string, string, string>? OnCrossChatRequested;
    public event Action<string, string, string, int>? OnCrossChatDialogueStarted;

    public void SetDraggingTab(string? tabId)
    {
        DraggedTabId = tabId;
        IsDraggingTab = !string.IsNullOrWhiteSpace(tabId);
        OnDragStateChanged?.Invoke();
    }

    public void SplitWithDraggedTab(string? tabId = null)
    {
        var targetTabId = tabId ?? DraggedTabId;
        IsDraggingTab = false;
        DraggedTabId = null;
        OnDragStateChanged?.Invoke();

        if (!string.IsNullOrWhiteSpace(targetTabId))
        {
            if (IsSplitView)
            {
                SecondaryTabId = targetTabId;
            }
            else
            {
                ToggleSplitView(targetTabId);
            }
            SaveTabs();
        }
    }

    public WorkspaceTabService(StorageService storageService)
    {
        _storageService = storageService;
        _tabsFilePath = _storageService.GetFilePath("open_tabs.json");
        LoadTabs();
    }

    public void ToggleSplitView(string? preferredSecondaryTabId = null)
    {
        if (IsSplitView)
        {
            IsSplitView = false;
            SecondaryTabId = null;
        }
        else
        {
            IsSplitView = true;
            if (!string.IsNullOrWhiteSpace(preferredSecondaryTabId) && OpenTabs.Any(t => t.Id == preferredSecondaryTabId))
            {
                SecondaryTabId = preferredSecondaryTabId;
            }
            else
            {
                // Default to a different tab or create a secondary tab
                var otherTab = OpenTabs.FirstOrDefault(t => t.Id != ActiveTabId);
                if (otherTab != null)
                {
                    SecondaryTabId = otherTab.Id;
                }
                else
                {
                    var newSecondary = new WorkspaceTab
                    {
                        Title = "Split chat",
                        Type = WorkspaceTabType.Chat,
                        ChatId = Guid.NewGuid().ToString("N")[..8],
                        Icon = null
                    };
                    OpenTabs.Add(newSecondary);
                    SecondaryTabId = newSecondary.Id;
                }
            }
        }
        SaveTabs();
    }

    public void SelectSecondaryTab(string tabId)
    {
        if (OpenTabs.Any(t => t.Id == tabId))
        {
            SecondaryTabId = tabId;
            IsSplitView = true;
            SaveTabs();
        }
    }

    public void CloseSplitView()
    {
        IsSplitView = false;
        SecondaryTabId = null;
        SaveTabs();
    }

    public void SwapSplitTabs()
    {
        if (IsSplitView && !string.IsNullOrWhiteSpace(SecondaryTabId))
        {
            var temp = ActiveTabId;
            ActiveTabId = SecondaryTabId;
            SecondaryTabId = temp;
            SaveTabs();
        }
    }

    public void RequestCrossChatMessage(string fromChatId, string toChatId, string messageText)
    {
        OnCrossChatRequested?.Invoke(fromChatId, toChatId, messageText);
    }

    public void RequestCrossChatDialogue(string chatId1, string chatId2, string topicPrompt, int rounds = 3)
    {
        OnCrossChatDialogueStarted?.Invoke(chatId1, chatId2, topicPrompt, rounds);
    }

    public void LoadTabs()
    {
        try
        {
            if (File.Exists(_tabsFilePath))
            {
                var json = File.ReadAllText(_tabsFilePath);
                var trimmed = json.TrimStart();
                if (trimmed.StartsWith("{"))
                {
                    var state = JsonSerializer.Deserialize<WorkspaceTabSessionState>(json);
                    if (state?.OpenTabs != null && state.OpenTabs.Count > 0)
                    {
                        OpenTabs = state.OpenTabs;
                        ActiveTabId = (!string.IsNullOrWhiteSpace(state.ActiveTabId) && OpenTabs.Any(t => t.Id == state.ActiveTabId))
                            ? state.ActiveTabId
                            : OpenTabs[0].Id;
                        IsSplitView = state.IsSplitView && OpenTabs.Any(t => t.Id == state.SecondaryTabId);
                        SecondaryTabId = IsSplitView ? state.SecondaryTabId : null;
                        return;
                    }
                }
                else if (trimmed.StartsWith("["))
                {
                    var loaded = JsonSerializer.Deserialize<List<WorkspaceTab>>(json);
                    if (loaded != null && loaded.Count > 0)
                    {
                        OpenTabs = loaded;
                        ActiveTabId = OpenTabs[0].Id;
                        return;
                    }
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
            var state = new WorkspaceTabSessionState
            {
                OpenTabs = OpenTabs,
                ActiveTabId = ActiveTabId,
                IsSplitView = IsSplitView,
                SecondaryTabId = SecondaryTabId
            };
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            var tmpPath = _tabsFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _tabsFilePath, overwrite: true);
        }
        catch {}
        OnTabsChanged?.Invoke();
    }

    public void SelectTab(string tabId)
    {
        if (OpenTabs.Any(t => t.Id == tabId))
        {
            ActiveTabId = tabId;
            SaveTabs();
        }
    }

    public void ReorderTab(string sourceTabId, string targetTabId)
    {
        if (string.IsNullOrWhiteSpace(sourceTabId) || string.IsNullOrWhiteSpace(targetTabId) || sourceTabId == targetTabId) return;

        var sourceIndex = OpenTabs.FindIndex(t => t.Id == sourceTabId);
        var targetIndex = OpenTabs.FindIndex(t => t.Id == targetTabId);

        if (sourceIndex >= 0 && targetIndex >= 0)
        {
            var tab = OpenTabs[sourceIndex];
            OpenTabs.RemoveAt(sourceIndex);
            OpenTabs.Insert(targetIndex, tab);
            SaveTabs();
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

    public void OpenPresentationTab(string projectId, string title, string? htmlPath = null)
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == WorkspaceTabType.Presentation && t.ProjectId == projectId);
        if (existing != null)
        {
            if (!string.IsNullOrWhiteSpace(htmlPath)) existing.PresentationPath = htmlPath;
            existing.Title = title;
            ActiveTabId = existing.Id;
        }
        else
        {
            var newTab = new WorkspaceTab
            {
                Title = title,
                Type = WorkspaceTabType.Presentation,
                ProjectId = projectId,
                PresentationPath = htmlPath,
                Icon = "📽️"
            };
            OpenTabs.Add(newTab);
            ActiveTabId = newTab.Id;
        }
        SaveTabs();
    }

    public void OpenTerminalTab()
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == WorkspaceTabType.Terminal);
        if (existing != null)
        {
            ActiveTabId = existing.Id;
        }
        else
        {
            var newTab = new WorkspaceTab
            {
                Title = "Terminal",
                Type = WorkspaceTabType.Terminal,
                Icon = "💻"
            };
            OpenTabs.Add(newTab);
            ActiveTabId = newTab.Id;
        }
        SaveTabs();
        OnTabsChanged?.Invoke();
    }

    public void CloseTab(string tabId)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return;

        var index = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);

        if (SecondaryTabId == tabId)
        {
            SecondaryTabId = null;
            IsSplitView = false;
        }

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

    public void CloseTabsForChat(string chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId)) return;

        var tabsToClose = OpenTabs.Where(t => t.Type == WorkspaceTabType.Chat && t.ChatId == chatId).ToList();
        if (tabsToClose.Count == 0) return;

        foreach (var tab in tabsToClose)
        {
            OpenTabs.Remove(tab);
        }

        // Clean up secondary tab if it was pointing to one of the closed tabs
        if (SecondaryTabId != null && tabsToClose.Any(t => t.Id == SecondaryTabId))
        {
            SecondaryTabId = null;
            IsSplitView = false;
        }

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
        else if (tabsToClose.Any(t => t.Id == ActiveTabId))
        {
            ActiveTabId = OpenTabs[0].Id;
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

    public void CloseOthers(string tabId)
    {
        var target = OpenTabs.FirstOrDefault(t => t.Id == tabId);
        if (target == null) return;

        OpenTabs.RemoveAll(t => t.Id != tabId && !t.IsPinned);
        ActiveTabId = target.Id;
        if (SecondaryTabId != null && !OpenTabs.Any(t => t.Id == SecondaryTabId))
        {
            SecondaryTabId = null;
            IsSplitView = false;
        }
        SaveTabs();
    }

    public void CloseToTheRight(string tabId)
    {
        var index = OpenTabs.FindIndex(t => t.Id == tabId);
        if (index < 0 || index >= OpenTabs.Count - 1) return;

        var tabsToClose = OpenTabs.Skip(index + 1).Where(t => !t.IsPinned).ToList();
        foreach (var tab in tabsToClose)
        {
            OpenTabs.Remove(tab);
        }

        if (!OpenTabs.Any(t => t.Id == ActiveTabId))
        {
            ActiveTabId = tabId;
        }
        if (SecondaryTabId != null && !OpenTabs.Any(t => t.Id == SecondaryTabId))
        {
            SecondaryTabId = null;
            IsSplitView = false;
        }
        SaveTabs();
    }

    public void CloseAll()
    {
        var pinnedTabs = OpenTabs.Where(t => t.IsPinned).ToList();
        OpenTabs.Clear();
        if (pinnedTabs.Count > 0)
        {
            OpenTabs.AddRange(pinnedTabs);
            ActiveTabId = OpenTabs[0].Id;
        }
        else
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
        SecondaryTabId = null;
        IsSplitView = false;
        SaveTabs();
    }

    public void CloseTabs(IEnumerable<string> tabIds)
    {
        var idsSet = tabIds.ToHashSet();
        if (idsSet.Count == 0) return;

        var tabsToClose = OpenTabs.Where(t => idsSet.Contains(t.Id) && !t.IsPinned).ToList();
        if (tabsToClose.Count == 0) return;

        foreach (var tab in tabsToClose)
        {
            OpenTabs.Remove(tab);
        }

        if (SecondaryTabId != null && tabsToClose.Any(t => t.Id == SecondaryTabId))
        {
            SecondaryTabId = null;
            IsSplitView = false;
        }

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
        else if (tabsToClose.Any(t => t.Id == ActiveTabId))
        {
            ActiveTabId = OpenTabs[0].Id;
        }

        SaveTabs();
    }

    public void TogglePinTab(string tabId)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab != null)
        {
            tab.IsPinned = !tab.IsPinned;
            SaveTabs();
        }
    }

    public WorkspaceTab? DuplicateTab(string tabId)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return null;

        var index = OpenTabs.IndexOf(tab);
        var copy = new WorkspaceTab
        {
            Title = tab.Title,
            Type = tab.Type,
            ChatId = tab.ChatId,
            ToolId = tab.ToolId,
            ProjectId = tab.ProjectId,
            PresentationPath = tab.PresentationPath,
            Icon = tab.Icon,
            IsPinned = false
        };

        OpenTabs.Insert(index + 1, copy);
        ActiveTabId = copy.Id;
        SaveTabs();
        return copy;
    }
}
