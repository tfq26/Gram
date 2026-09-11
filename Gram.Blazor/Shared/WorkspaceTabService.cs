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
    public event Action<string>? OnTabClosed;
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
        var draggedId = DraggedTabId;
        var targetId = tabId;
        IsDraggingTab = false;
        DraggedTabId = null;
        OnDragStateChanged?.Invoke();

        if (!string.IsNullOrWhiteSpace(draggedId))
        {
            var primaryTab = !string.IsNullOrWhiteSpace(targetId) && targetId != draggedId
                ? OpenTabs.FirstOrDefault(t => t.Id == targetId)
                : OpenTabs.FirstOrDefault(t => t.Id != draggedId);

            if (primaryTab != null)
            {
                LinkTabs(primaryTab.Id, draggedId);
            }
            else
            {
                ToggleSplitView(draggedId);
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

    public void LinkTabs(string primaryTabId, string secondaryTabId)
    {
        if (string.IsNullOrWhiteSpace(primaryTabId) || string.IsNullOrWhiteSpace(secondaryTabId) || primaryTabId == secondaryTabId) return;

        var tabA = OpenTabs.FirstOrDefault(t => t.Id == primaryTabId);
        var tabB = OpenTabs.FirstOrDefault(t => t.Id == secondaryTabId);
        if (tabA == null || tabB == null) return;

        // Clear any prior links on either tab
        if (!string.IsNullOrEmpty(tabA.SplitLinkedTabId))
        {
            var oldA = OpenTabs.FirstOrDefault(t => t.Id == tabA.SplitLinkedTabId);
            if (oldA != null) oldA.SplitLinkedTabId = null;
        }
        if (!string.IsNullOrEmpty(tabB.SplitLinkedTabId))
        {
            var oldB = OpenTabs.FirstOrDefault(t => t.Id == tabB.SplitLinkedTabId);
            if (oldB != null) oldB.SplitLinkedTabId = null;
        }

        tabA.SplitLinkedTabId = tabB.Id;
        tabB.SplitLinkedTabId = tabA.Id;

        // Keep tabB directly adjacent to tabA
        OpenTabs.Remove(tabB);
        var idxA = OpenTabs.IndexOf(tabA);
        OpenTabs.Insert(idxA + 1, tabB);

        ActiveTabId = tabA.Id;
        SecondaryTabId = tabB.Id;
        IsSplitView = true;

        SaveTabs();
    }

    public void UnlinkTabs(string tabId)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return;

        var partner = !string.IsNullOrEmpty(tab.SplitLinkedTabId)
            ? OpenTabs.FirstOrDefault(t => t.Id == tab.SplitLinkedTabId)
            : null;

        tab.SplitLinkedTabId = null;
        if (partner != null) partner.SplitLinkedTabId = null;

        IsSplitView = false;
        SecondaryTabId = null;

        SaveTabs();
    }

    public WorkspaceTab? GetSplitLeftTab()
    {
        if (!IsSplitView || ActiveTab == null) return ActiveTab;
        var partner = SecondaryTab;
        if (partner == null) return ActiveTab;

        var idxActive = OpenTabs.IndexOf(ActiveTab);
        var idxPartner = OpenTabs.IndexOf(partner);
        return idxActive <= idxPartner ? ActiveTab : partner;
    }

    public WorkspaceTab? GetSplitRightTab()
    {
        if (!IsSplitView || ActiveTab == null) return SecondaryTab;
        var partner = SecondaryTab;
        if (partner == null) return null;

        var idxActive = OpenTabs.IndexOf(ActiveTab);
        var idxPartner = OpenTabs.IndexOf(partner);
        return idxActive <= idxPartner ? partner : ActiveTab;
    }

    public void ToggleSplitView(string? preferredSecondaryTabId = null)
    {
        var active = ActiveTab;
        if (active != null && !string.IsNullOrEmpty(active.SplitLinkedTabId))
        {
            UnlinkTabs(active.Id);
            return;
        }

        if (IsSplitView)
        {
            IsSplitView = false;
            SecondaryTabId = null;
            SaveTabs();
            return;
        }

        var secondary = !string.IsNullOrWhiteSpace(preferredSecondaryTabId)
            ? OpenTabs.FirstOrDefault(t => t.Id == preferredSecondaryTabId)
            : OpenTabs.FirstOrDefault(t => t.Id != ActiveTabId);

        if (secondary != null && active != null)
        {
            LinkTabs(active.Id, secondary.Id);
        }
        else if (active != null)
        {
            var newSecondary = new WorkspaceTab
            {
                Title = "Split chat",
                Type = WorkspaceTabType.Chat,
                ChatId = Guid.NewGuid().ToString("N")[..8],
                Icon = null
            };
            OpenTabs.Add(newSecondary);
            LinkTabs(active.Id, newSecondary.Id);
        }
    }

    public void SelectSecondaryTab(string tabId)
    {
        if (OpenTabs.Any(t => t.Id == tabId))
        {
            if (ActiveTab != null && ActiveTabId != tabId)
            {
                LinkTabs(ActiveTabId, tabId);
            }
            else
            {
                SecondaryTabId = tabId;
                IsSplitView = true;
                SaveTabs();
            }
        }
    }

    public void CloseSplitView()
    {
        if (ActiveTab != null && !string.IsNullOrEmpty(ActiveTab.SplitLinkedTabId))
        {
            UnlinkTabs(ActiveTab.Id);
        }
        else
        {
            IsSplitView = false;
            SecondaryTabId = null;
            SaveTabs();
        }
    }

    public void SwapSplitTabs()
    {
        if (IsSplitView && !string.IsNullOrWhiteSpace(SecondaryTabId))
        {
            var tabA = ActiveTab;
            var tabB = SecondaryTab;
            if (tabA != null && tabB != null)
            {
                var idxA = OpenTabs.IndexOf(tabA);
                var idxB = OpenTabs.IndexOf(tabB);
                if (idxA >= 0 && idxB >= 0)
                {
                    OpenTabs[idxA] = tabB;
                    OpenTabs[idxB] = tabA;
                }
            }

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

                        var active = OpenTabs.FirstOrDefault(t => t.Id == ActiveTabId);
                        if (active != null && !string.IsNullOrEmpty(active.SplitLinkedTabId) && OpenTabs.Any(t => t.Id == active.SplitLinkedTabId))
                        {
                            IsSplitView = true;
                            SecondaryTabId = active.SplitLinkedTabId;
                        }
                        else
                        {
                            IsSplitView = state.IsSplitView && OpenTabs.Any(t => t.Id == state.SecondaryTabId);
                            SecondaryTabId = IsSplitView ? state.SecondaryTabId : null;
                        }
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
        var tab = OpenTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab != null)
        {
            ActiveTabId = tabId;
            if (!string.IsNullOrEmpty(tab.SplitLinkedTabId))
            {
                var partner = OpenTabs.FirstOrDefault(t => t.Id == tab.SplitLinkedTabId);
                if (partner != null)
                {
                    IsSplitView = true;
                    SecondaryTabId = partner.Id;
                }
                else
                {
                    tab.SplitLinkedTabId = null;
                    IsSplitView = false;
                    SecondaryTabId = null;
                }
            }
            else
            {
                IsSplitView = false;
                SecondaryTabId = null;
            }
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

    public void OpenGitTab()
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == WorkspaceTabType.Git);
        if (existing != null)
        {
            ActiveTabId = existing.Id;
        }
        else
        {
            var newTab = new WorkspaceTab
            {
                Title = "Source Control",
                Type = WorkspaceTabType.Git,
                Icon = "⌥"
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

        // If part of a split pair, unlink partner
        if (!string.IsNullOrEmpty(tab.SplitLinkedTabId))
        {
            var partner = OpenTabs.FirstOrDefault(t => t.Id == tab.SplitLinkedTabId);
            if (partner != null)
            {
                partner.SplitLinkedTabId = null;
                if (ActiveTabId == tabId)
                {
                    ActiveTabId = partner.Id;
                }
            }
        }

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
            IsSplitView = false;
            SecondaryTabId = null;
        }
        else if (ActiveTabId == tabId)
        {
            var newIndex = Math.Min(index, OpenTabs.Count - 1);
            ActiveTabId = OpenTabs[newIndex].Id;
            var newActive = OpenTabs[newIndex];
            if (!string.IsNullOrEmpty(newActive.SplitLinkedTabId))
            {
                SecondaryTabId = newActive.SplitLinkedTabId;
                IsSplitView = true;
            }
            else
            {
                SecondaryTabId = null;
                IsSplitView = false;
            }
        }

        OnTabClosed?.Invoke(tabId);
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

    public void UpdateChatTabId(string oldChatId, string newChatId, string? newTitle = null)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Type == WorkspaceTabType.Chat && t.ChatId == oldChatId);
        if (tab != null)
        {
            tab.ChatId = newChatId;
            if (!string.IsNullOrWhiteSpace(newTitle)) tab.Title = newTitle;
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
