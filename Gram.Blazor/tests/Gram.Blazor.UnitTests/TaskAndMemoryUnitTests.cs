using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class TaskAndMemoryUnitTests
{
    [Fact]
    public async Task TaskManagement_CreateListCompleteDelete()
    {
        using var tempDir = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempDir.Path);

        using var service = new TaskAndMemoryService();

        // 1. Create task
        var task = await service.CreateTaskAsync("Review API PR", "Check security auth tokens", priority: "high");
        Assert.NotNull(task);
        Assert.Equal("Review API PR", task.Title);
        Assert.Equal("high", task.Priority);
        Assert.Equal("todo", task.Status);

        // 2. List tasks
        var list = await service.GetTasksAsync();
        Assert.Single(list);
        Assert.Equal(task.Id, list[0].Id);

        // 3. Mark completed
        var updated = await service.UpdateTaskStatusAsync(task.Id, "done");
        Assert.True(updated);

        var activeList = await service.GetTasksAsync(includeCompleted: false);
        Assert.Empty(activeList);

        // 4. Delete task
        var deleted = await service.DeleteTaskAsync(task.Id);
        Assert.True(deleted);

        var allList = await service.GetTasksAsync(includeCompleted: true);
        Assert.Empty(allList);
    }

    [Fact]
    public async Task MemoryRules_AddListDelete_InjectsIntoSystemPrompt()
    {
        using var tempDir = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempDir.Path);

        using var memoryService = new TaskAndMemoryService();

        // Add persistent rule
        var rule = await memoryService.AddMemoryRuleAsync("Always write clean .NET 9 C# code with async/await.", "instruction");
        Assert.NotNull(rule);
        Assert.Equal("instruction", rule.Category);

        // Verify prompt injection
        var promptContext = memoryService.GetSystemPromptMemoryContext();
        Assert.Contains("<user_persistent_memory_and_rules>", promptContext);
        Assert.Contains("Always write clean .NET 9 C# code", promptContext);

        // ContextBuilder integration test
        var storage = new StorageService();
        var sessionStorage = new SessionStorageService(storage);
        var config = new ConfigurationBuilder().Build();
        var settings = new SettingsService(config, storage);
        var repoMap = new RepositoryMapService();
        var contextBuilder = new ContextBuilder(sessionStorage, settings, repoMap, memoryService);

        var conv = new Conversation { Id = "test-session", Title = "Test Chat" };
        var result = await contextBuilder.AssembleContextAsync(conv);

        Assert.Contains("<user_persistent_memory_and_rules>", result.SystemPrompt);
        Assert.Contains("Always write clean .NET 9 C# code", result.SystemPrompt);

        // Delete rule
        var deleted = await memoryService.DeleteMemoryRuleAsync(rule.Id);
        Assert.True(deleted);

        var emptyContext = memoryService.GetSystemPromptMemoryContext();
        Assert.DoesNotContain("Always write clean .NET 9 C# code", emptyContext);
    }

    [Fact]
    public async Task Reminders_CreateAndDismiss()
    {
        using var tempDir = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempDir.Path);

        using var service = new TaskAndMemoryService();

        var reminder = await service.CreateReminderAsync("Deploy to Railway", DateTimeOffset.UtcNow.AddMinutes(30));
        Assert.NotNull(reminder);
        Assert.False(reminder.IsDismissed);

        var list = await service.GetRemindersAsync();
        Assert.Single(list);

        var dismissed = await service.DismissReminderAsync(reminder.Id);
        Assert.True(dismissed);

        var pendingList = await service.GetRemindersAsync(includeDismissed: false);
        Assert.Empty(pendingList);
    }

    [Fact]
    public async Task AgentTools_ManageTasks_Execution()
    {
        using var tempDir = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempDir.Path);

        using var taskService = new TaskAndMemoryService();
        var storage = new StorageService();
        var gitService = new GitService(storage);
        var securityService = new AgentSecurityService(storage);
        var toolboxService = new ToolboxManagerService(storage);
        var toolKernel = new GramToolKernel(securityService, toolboxService, Enumerable.Empty<IGramTool>());
        var webContent = new WebContentService(new SimpleFactory());

        var registry = new AgentToolRegistry(
            gitService, 
            securityService, 
            toolboxService, 
            toolKernel, 
            webContent, 
            taskAndMemoryService: taskService);

        // Execute manage_tasks create
        var createArgs = new JsonObject
        {
            ["action"] = "create",
            ["title"] = "Ship Voice UI feature",
            ["priority"] = "urgent"
        }.ToJsonString();

        var execResult = await registry.ExecuteToolAsync("manage_tasks", "call-1", createArgs, tempDir.Path);
        Assert.False(execResult.IsError);
        Assert.Contains("Ship Voice UI feature", execResult.Output);

        // Verify task exists in service
        var tasks = await taskService.GetTasksAsync();
        Assert.Single(tasks);
        Assert.Equal("Ship Voice UI feature", tasks[0].Title);
        Assert.Equal("urgent", tasks[0].Priority);
    }

    [Fact]
    public async Task ConversationalSimulation_UserInstructsRule_RememberedAcrossTurns()
    {
        using var tempDir = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempDir.Path);

        using var memoryService = new TaskAndMemoryService();
        var storage = new StorageService();
        var gitService = new GitService(storage);
        var securityService = new AgentSecurityService(storage);
        var toolboxService = new ToolboxManagerService(storage);
        var toolKernel = new GramToolKernel(securityService, toolboxService, Enumerable.Empty<IGramTool>());
        var webContent = new WebContentService(new SimpleFactory());

        var registry = new AgentToolRegistry(
            gitService, 
            securityService, 
            toolboxService, 
            toolKernel, 
            webContent, 
            taskAndMemoryService: memoryService);

        // Simulating Turn 1: User says "Gram, remember that our production domain is gram.2helix.org"
        var rememberCallArgs = new JsonObject
        {
            ["action"] = "add",
            ["ruleContent"] = "Production domain is gram.2helix.org",
            ["category"] = "project_context"
        }.ToJsonString();

        var result1 = await registry.ExecuteToolAsync("remember_rule", "call-mem-1", rememberCallArgs, tempDir.Path);
        Assert.False(result1.IsError);
        Assert.Contains("gram.2helix.org", result1.Output);

        // Simulating Turn 2 in a brand new chat session: ContextBuilder automatically assembles persistent context
        var sessionStorage = new SessionStorageService(storage);
        var config = new ConfigurationBuilder().Build();
        var settings = new SettingsService(config, storage);
        var repoMap = new RepositoryMapService();
        var contextBuilder = new ContextBuilder(sessionStorage, settings, repoMap, memoryService);

        var newChatConversation = new Conversation { Id = "brand-new-chat-session", Title = "Infrastructure Query" };
        var assembledContext = await contextBuilder.AssembleContextAsync(newChatConversation);

        Assert.Contains("<user_persistent_memory_and_rules>", assembledContext.SystemPrompt);
        Assert.Contains("Production domain is gram.2helix.org", assembledContext.SystemPrompt);
        Assert.Contains("[PROJECT_CONTEXT]", assembledContext.SystemPrompt);
    }

    [Fact]
    public async Task ConversationalSimulation_UserRequestsTaskCreationAndCheckoff()
    {
        using var tempDir = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempDir.Path);

        using var taskService = new TaskAndMemoryService();
        var storage = new StorageService();
        var gitService = new GitService(storage);
        var securityService = new AgentSecurityService(storage);
        var toolboxService = new ToolboxManagerService(storage);
        var toolKernel = new GramToolKernel(securityService, toolboxService, Enumerable.Empty<IGramTool>());
        var webContent = new WebContentService(new SimpleFactory());

        var registry = new AgentToolRegistry(
            gitService, 
            securityService, 
            toolboxService, 
            toolKernel, 
            webContent, 
            taskAndMemoryService: taskService);

        // Simulating Turn 1: User says "Create 2 tasks: 'Write unit tests' and 'Refactor CSS'"
        var task1Args = new JsonObject { ["action"] = "create", ["title"] = "Write unit tests", ["priority"] = "high" }.ToJsonString();
        var task2Args = new JsonObject { ["action"] = "create", ["title"] = "Refactor CSS", ["priority"] = "medium" }.ToJsonString();

        var res1 = await registry.ExecuteToolAsync("manage_tasks", "call-t1", task1Args, tempDir.Path);
        var res2 = await registry.ExecuteToolAsync("manage_tasks", "call-t2", task2Args, tempDir.Path);
        Assert.False(res1.IsError);
        Assert.False(res2.IsError);

        var tasks = await taskService.GetTasksAsync();
        Assert.Equal(2, tasks.Count);

        var testTask = tasks.First(t => t.Title == "Write unit tests");

        // Simulating Turn 2: User says "I finished writing unit tests, mark it as done"
        var updateArgs = new JsonObject
        {
            ["action"] = "update_status",
            ["taskId"] = testTask.Id,
            ["status"] = "done"
        }.ToJsonString();

        var res3 = await registry.ExecuteToolAsync("manage_tasks", "call-t3", updateArgs, tempDir.Path);
        Assert.False(res3.IsError);
        Assert.Contains("status updated to done", res3.Output);

        var updatedTasks = await taskService.GetTasksAsync();
        var updatedTestTask = updatedTasks.First(t => t.Id == testTask.Id);
        Assert.Equal("done", updatedTestTask.Status);
        Assert.NotNull(updatedTestTask.CompletedAt);
    }

    [Fact]
    public async Task ConversationalSimulation_UserRequestsRelativeReminder()
    {
        using var tempDir = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempDir.Path);

        using var memoryService = new TaskAndMemoryService();
        var storage = new StorageService();
        var gitService = new GitService(storage);
        var securityService = new AgentSecurityService(storage);
        var toolboxService = new ToolboxManagerService(storage);
        var toolKernel = new GramToolKernel(securityService, toolboxService, Enumerable.Empty<IGramTool>());
        var webContent = new WebContentService(new SimpleFactory());

        var registry = new AgentToolRegistry(
            gitService, 
            securityService, 
            toolboxService, 
            toolKernel, 
            webContent, 
            taskAndMemoryService: memoryService);

        // Simulating: User says "Remind me in 1 hour to test the voice call"
        var reminderArgs = new JsonObject
        {
            ["action"] = "set",
            ["title"] = "Test the voice call",
            ["remindAt"] = "in 1 hour"
        }.ToJsonString();

        var res = await registry.ExecuteToolAsync("set_reminder", "call-rem-1", reminderArgs, tempDir.Path);
        Assert.False(res.IsError);
        Assert.Contains("Reminder set", res.Output);
        Assert.Contains("Test the voice call", res.Output);

        var reminders = await memoryService.GetRemindersAsync();
        Assert.Single(reminders);
        Assert.Equal("Test the voice call", reminders[0].Title);
        Assert.True(reminders[0].RemindAt > DateTimeOffset.UtcNow.AddMinutes(45));
    }

    [Fact]
    public void OpenAiFunctionSchema_Includes_TasksAndMemoryTools()
    {
        var storage = new StorageService();
        var gitService = new GitService(storage);
        var securityService = new AgentSecurityService(storage);
        var toolboxService = new ToolboxManagerService(storage);
        var toolKernel = new GramToolKernel(securityService, toolboxService, Enumerable.Empty<IGramTool>());
        var webContent = new WebContentService(new SimpleFactory());
        var memoryService = new TaskAndMemoryService();

        var registry = new AgentToolRegistry(
            gitService, 
            securityService, 
            toolboxService, 
            toolKernel, 
            webContent, 
            taskAndMemoryService: memoryService);

        var tools = registry.GetAvailableTools(isLocalEnvironment: true);

        // Verify manage_tasks tool schema
        var taskTool = tools.FirstOrDefault(t => t.Name == "manage_tasks");
        Assert.NotNull(taskTool);
        var taskSchema = taskTool.ToOpenAiFunctionSchema();
        Assert.Equal("manage_tasks", taskSchema["function"]?["name"]?.ToString());
        Assert.NotNull(taskSchema["function"]?["parameters"]);

        // Verify remember_rule tool schema
        var ruleTool = tools.FirstOrDefault(t => t.Name == "remember_rule");
        Assert.NotNull(ruleTool);
        var ruleSchema = ruleTool.ToOpenAiFunctionSchema();
        Assert.Equal("remember_rule", ruleSchema["function"]?["name"]?.ToString());

        // Verify set_reminder tool schema
        var reminderTool = tools.FirstOrDefault(t => t.Name == "set_reminder");
        Assert.NotNull(reminderTool);
        var reminderSchema = reminderTool.ToOpenAiFunctionSchema();
        Assert.Equal("set_reminder", reminderSchema["function"]?["name"]?.ToString());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gram-test-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { }
        }
    }

    private sealed class SimpleFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
