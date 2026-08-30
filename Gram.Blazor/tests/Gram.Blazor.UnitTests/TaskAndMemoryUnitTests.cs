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
