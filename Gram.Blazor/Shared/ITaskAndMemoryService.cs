using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Gram.Blazor.Shared;

public interface ITaskAndMemoryService
{
    event Action? OnChanged;

    Task<List<GramTaskItem>> GetTasksAsync(string? conversationId = null, bool includeCompleted = true);
    Task<GramTaskItem> CreateTaskAsync(string title, string? description = null, string priority = "medium", DateTimeOffset? dueDate = null, string? conversationId = null);
    Task<bool> UpdateTaskStatusAsync(string taskId, string status);
    Task<bool> DeleteTaskAsync(string taskId);

    Task<List<GramMemoryRule>> GetActiveMemoryRulesAsync();
    Task<GramMemoryRule> AddMemoryRuleAsync(string content, string category = "preference");
    Task<bool> DeleteMemoryRuleAsync(string ruleId);

    Task<List<GramReminder>> GetRemindersAsync(bool includeDismissed = false);
    Task<GramReminder> CreateReminderAsync(string title, DateTimeOffset remindAt);
    Task<bool> DismissReminderAsync(string reminderId);

    string GetSystemPromptMemoryContext();
}
