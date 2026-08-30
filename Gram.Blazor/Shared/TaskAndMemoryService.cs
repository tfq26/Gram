using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Gram.Blazor.Shared;

public class TaskAndMemoryService : ITaskAndMemoryService, IDisposable
{
    private readonly string _storageDir;
    private readonly string _tasksFilePath;
    private readonly string _memoryFilePath;
    private readonly string _remindersFilePath;

    private readonly List<GramTaskItem> _tasks = new();
    private readonly List<GramMemoryRule> _memoryRules = new();
    private readonly List<GramReminder> _reminders = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Timer? _reminderTimer;
    private bool _isLoaded = false;

    public event Action? OnChanged;

    public TaskAndMemoryService()
    {
        var baseDir = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = string.IsNullOrWhiteSpace(home) ? AppDomain.CurrentDomain.BaseDirectory : Path.Combine(home, ".gram");
        }

        _storageDir = Path.Combine(baseDir, "memory_vault");
        _tasksFilePath = Path.Combine(_storageDir, "tasks.json");
        _memoryFilePath = Path.Combine(_storageDir, "rules.json");
        _remindersFilePath = Path.Combine(_storageDir, "reminders.json");

        try
        {
            Directory.CreateDirectory(_storageDir);
        }
        catch { }

        // Start reminder evaluation timer every 30 seconds
        _reminderTimer = new Timer(EvaluateRemindersCallback, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
    }

    private async Task EnsureLoadedAsync()
    {
        if (_isLoaded) return;

        await _lock.WaitAsync();
        try
        {
            if (_isLoaded) return;

            if (File.Exists(_tasksFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_tasksFilePath);
                    var items = JsonSerializer.Deserialize<List<GramTaskItem>>(json);
                    if (items != null)
                    {
                        _tasks.Clear();
                        _tasks.AddRange(items);
                    }
                }
                catch { }
            }

            if (File.Exists(_memoryFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_memoryFilePath);
                    var rules = JsonSerializer.Deserialize<List<GramMemoryRule>>(json);
                    if (rules != null)
                    {
                        _memoryRules.Clear();
                        _memoryRules.AddRange(rules);
                    }
                }
                catch { }
            }

            if (File.Exists(_remindersFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_remindersFilePath);
                    var rems = JsonSerializer.Deserialize<List<GramReminder>>(json);
                    if (rems != null)
                    {
                        _reminders.Clear();
                        _reminders.AddRange(rems);
                    }
                }
                catch { }
            }

            _isLoaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            Directory.CreateDirectory(_storageDir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(_tasksFilePath, JsonSerializer.Serialize(_tasks, options));
            await File.WriteAllTextAsync(_memoryFilePath, JsonSerializer.Serialize(_memoryRules, options));
            await File.WriteAllTextAsync(_remindersFilePath, JsonSerializer.Serialize(_reminders, options));
        }
        catch { }

        OnChanged?.Invoke();
    }

    public async Task<List<GramTaskItem>> GetTasksAsync(string? conversationId = null, bool includeCompleted = true)
    {
        await EnsureLoadedAsync();
        await _lock.WaitAsync();
        try
        {
            var query = _tasks.AsEnumerable();
            if (!string.IsNullOrEmpty(conversationId))
            {
                query = query.Where(t => t.ConversationId == conversationId || t.ConversationId == null);
            }
            if (!includeCompleted)
            {
                query = query.Where(t => t.Status != "done");
            }
            return query.OrderByDescending(t => t.CreatedAt).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<GramTaskItem> CreateTaskAsync(string title, string? description = null, string priority = "medium", DateTimeOffset? dueDate = null, string? conversationId = null)
    {
        await EnsureLoadedAsync();
        var item = new GramTaskItem
        {
            Title = title.Trim(),
            Description = description?.Trim(),
            Priority = string.IsNullOrWhiteSpace(priority) ? "medium" : priority.ToLowerInvariant(),
            DueDate = dueDate,
            ConversationId = conversationId,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "todo"
        };

        await _lock.WaitAsync();
        try
        {
            _tasks.Insert(0, item);
            await SaveAsync();
        }
        finally
        {
            _lock.Release();
        }

        return item;
    }

    public async Task<bool> UpdateTaskStatusAsync(string taskId, string status)
    {
        await EnsureLoadedAsync();
        await _lock.WaitAsync();
        try
        {
            var task = _tasks.FirstOrDefault(t => t.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
            if (task == null) return false;

            task.Status = status.ToLowerInvariant();
            if (task.Status == "done")
            {
                task.CompletedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                task.CompletedAt = null;
            }

            await SaveAsync();
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteTaskAsync(string taskId)
    {
        await EnsureLoadedAsync();
        await _lock.WaitAsync();
        try
        {
            var removed = _tasks.RemoveAll(t => t.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                await SaveAsync();
                return true;
            }
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<GramMemoryRule>> GetActiveMemoryRulesAsync()
    {
        await EnsureLoadedAsync();
        await _lock.WaitAsync();
        try
        {
            return _memoryRules.Where(r => r.IsActive).OrderByDescending(r => r.CreatedAt).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<GramMemoryRule> AddMemoryRuleAsync(string content, string category = "preference")
    {
        await EnsureLoadedAsync();
        var rule = new GramMemoryRule
        {
            RuleContent = content.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? "preference" : category.ToLowerInvariant(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _lock.WaitAsync();
        try
        {
            _memoryRules.Insert(0, rule);
            await SaveAsync();
        }
        finally
        {
            _lock.Release();
        }

        return rule;
    }

    public async Task<bool> DeleteMemoryRuleAsync(string ruleId)
    {
        await EnsureLoadedAsync();
        await _lock.WaitAsync();
        try
        {
            var removed = _memoryRules.RemoveAll(r => r.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                await SaveAsync();
                return true;
            }
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<GramReminder>> GetRemindersAsync(bool includeDismissed = false)
    {
        await EnsureLoadedAsync();
        await _lock.WaitAsync();
        try
        {
            var query = _reminders.AsEnumerable();
            if (!includeDismissed)
            {
                query = query.Where(r => !r.IsDismissed);
            }
            return query.OrderBy(r => r.RemindAt).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<GramReminder> CreateReminderAsync(string title, DateTimeOffset remindAt)
    {
        await EnsureLoadedAsync();
        var reminder = new GramReminder
        {
            Title = title.Trim(),
            RemindAt = remindAt,
            CreatedAt = DateTimeOffset.UtcNow,
            IsDismissed = false
        };

        await _lock.WaitAsync();
        try
        {
            _reminders.Add(reminder);
            await SaveAsync();
        }
        finally
        {
            _lock.Release();
        }

        return reminder;
    }

    public async Task<bool> DismissReminderAsync(string reminderId)
    {
        await EnsureLoadedAsync();
        await _lock.WaitAsync();
        try
        {
            var reminder = _reminders.FirstOrDefault(r => r.Id.Equals(reminderId, StringComparison.OrdinalIgnoreCase));
            if (reminder == null) return false;

            reminder.IsDismissed = true;
            await SaveAsync();
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public string GetSystemPromptMemoryContext()
    {
        if (!_isLoaded)
        {
            // Sync read fallback if not yet loaded
            try
            {
                EnsureLoadedAsync().GetAwaiter().GetResult();
            }
            catch { }
        }

        var sb = new StringBuilder();

        lock (_memoryRules)
        {
            var activeRules = _memoryRules.Where(r => r.IsActive).ToList();
            if (activeRules.Count > 0)
            {
                sb.AppendLine("<user_persistent_memory_and_rules>");
                sb.AppendLine("The user has instructed Gram to remember the following persistent rules, preferences, and project facts across conversations:");
                foreach (var rule in activeRules)
                {
                    sb.AppendLine($"- [{rule.Category.ToUpperInvariant()}]: {rule.RuleContent}");
                }
                sb.AppendLine("</user_persistent_memory_and_rules>");
            }
        }

        lock (_tasks)
        {
            var openTasks = _tasks.Where(t => t.Status != "done").Take(10).ToList();
            if (openTasks.Count > 0)
            {
                sb.AppendLine("<user_active_tasks>");
                sb.AppendLine("Current active user tasks:");
                foreach (var task in openTasks)
                {
                    var due = task.DueDate.HasValue ? $" (Due: {task.DueDate.Value:yyyy-MM-dd HH:mm})" : "";
                    sb.AppendLine($"- [{task.Priority.ToUpperInvariant()}] {task.Title} [Status: {task.Status}]{due}");
                }
                sb.AppendLine("</user_active_tasks>");
            }
        }

        return sb.ToString();
    }

    private void EvaluateRemindersCallback(object? state)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            List<GramReminder> dueReminders;
            lock (_reminders)
            {
                dueReminders = _reminders.Where(r => !r.IsDismissed && r.TriggeredAt == null && r.RemindAt <= now).ToList();
                foreach (var rem in dueReminders)
                {
                    rem.TriggeredAt = now;
                }
            }

            if (dueReminders.Count > 0)
            {
                _ = SaveAsync();
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _reminderTimer?.Dispose();
        _lock.Dispose();
    }
}
