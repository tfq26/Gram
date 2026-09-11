using Gram.Desktop.Services;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class TopBarServiceTests
{
    [Fact]
    public void SetContext_RegistersActionsAndFiresChangedEvent()
    {
        var service = new TopBarService();
        var eventFired = false;
        service.OnChanged += () => eventFired = true;

        var actions = new[]
        {
            new TopBarAction("clear", "Clear", "Clear terminal", Shortcut: "⌘K"),
            new TopBarAction("restart", "Restart", "Restart shell")
        };

        service.SetContext("tab-1", actions);

        Assert.True(eventFired);
        Assert.Equal(2, service.ContextActions.Count);
        Assert.Equal("clear", service.ContextActions[0].Id);
        Assert.Equal("restart", service.ContextActions[1].Id);
    }

    [Fact]
    public void ClearContext_WithMatchingOwnerId_ClearsActions()
    {
        var service = new TopBarService();
        service.SetContext("tab-1", new[] { new TopBarAction("action1", "Action 1") });

        Assert.Single(service.ContextActions);

        service.ClearContext("tab-1");
        Assert.Empty(service.ContextActions);
    }

    [Fact]
    public void ClearContext_WithNonMatchingOwnerId_DoesNotClear()
    {
        var service = new TopBarService();
        service.SetContext("tab-1", new[] { new TopBarAction("action1", "Action 1") });

        service.ClearContext("tab-2"); // Different owner
        Assert.Single(service.ContextActions);
        Assert.Equal("action1", service.ContextActions[0].Id);
    }

    [Fact]
    public void ClearContext_WithNullOwnerId_ForcesClear()
    {
        var service = new TopBarService();
        service.SetContext("tab-1", new[] { new TopBarAction("action1", "Action 1") });

        service.ClearContext(null); // Unconditional clear
        Assert.Empty(service.ContextActions);
    }
}
