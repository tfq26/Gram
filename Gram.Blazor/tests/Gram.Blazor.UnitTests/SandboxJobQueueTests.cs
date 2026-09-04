using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SandboxJobQueueTests
{
    [Fact]
    public async Task Enqueue_IsIdempotentForSameKey()
    {
        var queue = new SandboxJobQueue();
        var first = await queue.EnqueueAsync(new SandboxJobRequest("/tmp/project", IdempotencyKey: "request-1"));
        var second = await queue.EnqueueAsync(new SandboxJobRequest("/tmp/different", IdempotencyKey: "request-1"));

        Assert.Equal(first.JobId, second.JobId);
        Assert.Equal(1, second.QueuePosition);
    }

    [Fact]
    public async Task Enqueue_ReportsPriorityAndResourceBudget()
    {
        var queue = new SandboxJobQueue();
        var job = await queue.EnqueueAsync(new SandboxJobRequest("/tmp/project", Priority: SandboxJobPriority.Interactive, ResourceClass: SandboxResourceClass.Medium, MaxRuntimeSeconds: 90));

        Assert.Equal(SandboxJobPriority.Interactive, job.Priority);
        Assert.Equal(SandboxResourceClass.Medium, job.ResourceClass);
        Assert.Equal(90, job.MaxRuntimeSeconds);
    }

}
