using System;
using System.Linq;
using Gram.Desktop.Services;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class CompanionTaskServiceTests
{
    [Fact]
    public void StartTask_AddsTaskToActiveAndRecentList()
    {
        using var service = new CompanionTaskService();

        var task = service.StartTask("task-1", "Coding Task", "Analyzing AST", targetConvId: "conv-123");

        Assert.Equal("task-1", task.Id);
        Assert.Equal("Coding Task", task.Title);
        Assert.Equal("Analyzing AST", task.Subtitle);
        Assert.Equal(CompanionTaskStatus.InProgress, task.Status);
        Assert.Equal("conv-123", task.TargetConversationId);

        Assert.Single(service.ActiveTasks);
        Assert.Equal("task-1", service.ActiveTasks[0].Id);
        Assert.Single(service.AllRecentTasks);
    }

    [Fact]
    public void ConcurrentTasks_TrackIndependently()
    {
        using var service = new CompanionTaskService();

        service.StartTask("task-chat", "Chat Agent", "Synthesizing", targetConvId: "chat-1");
        service.StartTask("task-build", "Terminal Build", "Compiling C#", targetTabId: "tab-term");

        Assert.Equal(2, service.ActiveTasks.Count);
        Assert.Contains(service.ActiveTasks, t => t.Id == "task-chat" && t.TargetConversationId == "chat-1");
        Assert.Contains(service.ActiveTasks, t => t.Id == "task-build" && t.TargetTabId == "tab-term");

        // Complete one task
        service.CompleteTask("task-build", "Build completed successfully");

        // Now only chat is in progress
        Assert.Single(service.ActiveTasks);
        Assert.Equal("task-chat", service.ActiveTasks[0].Id);

        // But both are in AllRecentTasks
        var recent = service.AllRecentTasks;
        Assert.Equal(2, recent.Count);
        var completedItem = recent.First(r => r.Id == "task-build");
        Assert.Equal(CompanionTaskStatus.Completed, completedItem.Status);
        Assert.NotNull(completedItem.CompletedAt);
    }

    [Fact]
    public void FailTask_SetsErrorStatusAndMessage()
    {
        using var service = new CompanionTaskService();

        service.StartTask("task-err", "Terminal Build", "Compiling", targetTabId: "tab-1");
        service.FailTask("task-err", "Syntax error at line 42");

        Assert.Empty(service.ActiveTasks);
        var recent = service.AllRecentTasks;
        Assert.Single(recent);
        var failed = recent[0];
        Assert.Equal(CompanionTaskStatus.Error, failed.Status);
        Assert.Equal("Syntax error at line 42", failed.ErrorMessage);
        Assert.Equal("Syntax error at line 42", failed.Subtitle);
    }

    [Fact]
    public void TriggerOneLinerForAnimation_SetsSpeech()
    {
        using var service = new CompanionTaskService();

        service.TriggerOneLinerForAnimation("smug");
        Assert.NotNull(service.CurrentSpeech);
        Assert.NotEmpty(service.CurrentSpeech);

        service.TriggerOneLinerForAnimation("eureka");
        Assert.NotNull(service.CurrentSpeech);

        service.DismissSpeech();
        Assert.Null(service.CurrentSpeech);
    }

    [Fact]
    public void DismissTask_RemovesTask()
    {
        using var service = new CompanionTaskService();

        service.StartTask("t-1", "Temp task");
        Assert.Single(service.ActiveTasks);

        service.DismissTask("t-1");
        Assert.Empty(service.ActiveTasks);
        Assert.Empty(service.AllRecentTasks);
    }
}
