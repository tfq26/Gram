using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class AgentWorkflowRouterTests
{
    [Fact]
    public async Task PresentationRequest_UsesPresentationWorkflowBeforeGenericChat()
    {
        var router = new AgentWorkflowRouter([
            new FakeWorkflow("chat", true),
            new FakeWorkflow("research", true),
            new FakeWorkflow("presentation", true)
        ]);
        var request = new AgentWorkflowRequest("gram-q-plus", "Create a presentation about batteries", [], "/tmp");

        AgentStepUpdate? update = null;
        await foreach (var item in router.RunAsync(request))
        {
            update = item;
            break;
        }

        Assert.Equal("presentation", update?.ContentDelta);
    }

    private sealed class FakeWorkflow(string name, bool canHandle) : IAgentWorkflow
    {
        public string Name => name;
        public bool CanHandle(AgentWorkflowRequest request) => canHandle;
        public async IAsyncEnumerable<AgentStepUpdate> RunAsync(AgentWorkflowRequest request)
        {
            yield return new AgentStepUpdate { ContentDelta = Name };
            await Task.CompletedTask;
        }
    }
}
