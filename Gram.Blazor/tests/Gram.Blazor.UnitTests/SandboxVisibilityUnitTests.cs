using System;
using System.IO;
using System.Threading.Tasks;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SandboxVisibilityUnitTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GramBoxService _gramBox;

    public SandboxVisibilityUnitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "gram_sb_vis_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // Initialize git repo for worktree testing
        using var initP = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "init") { WorkingDirectory = _tempDir });
        initP?.WaitForExit();
        using var configP1 = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "config user.name Test") { WorkingDirectory = _tempDir });
        configP1?.WaitForExit();
        using var configP2 = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "config user.email test@test.com") { WorkingDirectory = _tempDir });
        configP2?.WaitForExit();
        using var commitP = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "commit --allow-empty -m init") { WorkingDirectory = _tempDir });
        commitP?.WaitForExit();

        var logger = NullLogger<GramBoxService>.Instance;
        var worktreeLogger = NullLogger<GramBoxWorktreeManager>.Instance;
        var driverLogger = NullLogger<NativeScratchpadDriver>.Instance;

        var worktreeManager = new GramBoxWorktreeManager(worktreeLogger, new StorageService(), Path.Combine(_tempDir, "wt"));
        var driver = new NativeScratchpadDriver(driverLogger);

        _gramBox = new GramBoxService(logger, worktreeManager, driver);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch {}
    }

    [Fact]
    public async Task CreateSandbox_AssignsOriginChat_AndTransitionsMajorState()
    {
        GramBoxSandboxSession? eventSession = null;
        SandboxMajorState? eventState = null;

        _gramBox.OnSandboxStateChanged += (session, state) =>
        {
            eventSession = session;
            eventState = state;
        };

        var session = await _gramBox.CreateSandboxAsync(
            baseRepoPath: _tempDir,
            branchName: "gram/test-vis",
            taskName: "UI Visibility Spike",
            originatingChatId: "chat-101");

        Assert.NotNull(session);
        Assert.Equal("chat-101", session.OriginatingChatId);
        Assert.Contains("chat-101", session.SubscribingChatIds);
        Assert.Equal(SandboxMajorState.Ready, session.MajorState);
        Assert.Equal("UI Visibility Spike", session.TaskDescription);

        Assert.NotNull(eventSession);
        Assert.Equal(session.Id, eventSession.Id);
        Assert.Equal(SandboxMajorState.Ready, eventState);

        // Test GetSandboxesForChat
        var chatSandboxes = _gramBox.GetSandboxesForChat("chat-101");
        Assert.Single(chatSandboxes);
        Assert.Equal(session.Id, chatSandboxes[0].Id);

        // Test subscribing another chat
        _gramBox.SubscribeChat(session.Id, "chat-202");
        Assert.Contains("chat-202", session.SubscribingChatIds);

        var subSandboxes = _gramBox.GetSandboxesForChat("chat-202");
        Assert.Single(subSandboxes);
        Assert.Equal(session.Id, subSandboxes[0].Id);

        // Test unsubscribing
        _gramBox.UnsubscribeChat(session.Id, "chat-202");
        Assert.DoesNotContain("chat-202", session.SubscribingChatIds);
        Assert.Empty(_gramBox.GetSandboxesForChat("chat-202"));

        // Close and test Disposed state
        await _gramBox.CloseSandboxAsync(session.Id);
        Assert.Equal(SandboxMajorState.Disposed, eventState);
    }
}
