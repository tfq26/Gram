using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class GitGramPolicyUnitTests
{
    [Fact]
    public async Task UnsupportedActionReturnsExplicitError()
    {
        var service = new GitGramService(new SonnerService());

        var result = await service.ExecuteControlledActionAsync("delete-production", null, Environment.CurrentDirectory);

        Assert.False(result.Success);
        Assert.Equal("unsupported_git_action", result.ErrorCode);
    }

    [Fact]
    public async Task DisabledAutonomousCommitsAreRejectedBeforeGitRuns()
    {
        var service = new GitGramService(new SonnerService())
        {
            IsAutonomousCommitEnabled = false
        };

        var result = await service.ExecuteControlledActionAsync("commit", "test: blocked", Environment.CurrentDirectory);

        Assert.False(result.Success);
        Assert.Equal("autonomous_commit_disabled", result.ErrorCode);
    }
}
