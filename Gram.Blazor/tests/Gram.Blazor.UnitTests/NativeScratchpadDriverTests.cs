using Gram.Blazor.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class NativeScratchpadDriverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gram_driver_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteCommand_StopsWhenOutputExceedsLimit()
    {
        Directory.CreateDirectory(_root);
        var driver = new NativeScratchpadDriver(NullLogger<NativeScratchpadDriver>.Instance, new GramBoxConfig { MaxOutputBytes = 32, MaxProcessTimeoutSeconds = 10 });
        var session = new GramBoxSandboxSession { Id = "sb-test", WorktreePath = _root };

        var result = await driver.ExecuteCommandAsync(session, "printf '1234567890123456789012345678901234567890'");

        Assert.False(result.Success);
        Assert.Contains("output exceeded", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteCommand_StopsWhenMemoryBudgetIsExceeded()
    {
        Directory.CreateDirectory(_root);
        var driver = new NativeScratchpadDriver(NullLogger<NativeScratchpadDriver>.Instance, new GramBoxConfig { MaxProcessTimeoutSeconds = 5 });
        var session = new GramBoxSandboxSession
        {
            Id = "sb-memory-test",
            WorktreePath = _root,
            ResourceBudget = new SandboxResourceBudget(SandboxResourceClass.Tiny, 1, 1, 1024 * 1024)
        };

        var result = await driver.ExecuteCommandAsync(session, "sleep 2");

        Assert.False(result.Success);
        Assert.Contains("resource budget", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
