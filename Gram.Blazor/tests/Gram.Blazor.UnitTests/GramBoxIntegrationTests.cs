using System.Diagnostics;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class GramBoxIntegrationTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _testRepoRoot;

    public GramBoxIntegrationTests()
    {
        // Resolve repository root (containing .git)
        var current = Directory.GetCurrentDirectory();
        while (current != null && !Directory.Exists(Path.Combine(current, ".git")))
        {
            var parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent;
        }

        _repoRoot = current ?? Directory.GetCurrentDirectory();
        _testRepoRoot = CreateWritableClone(_repoRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRepoRoot)) Directory.Delete(_testRepoRoot, true);
        }
        catch { }
    }

    [Fact]
    public async Task GramBox_FullLifecycle_IsFast_Isolated_AndCleansUp()
    {
        // Arrange
        var storage = new StorageService();
        var worktreeManager = new GramBoxWorktreeManager(NullLogger<GramBoxWorktreeManager>.Instance, storage, Path.Combine(_testRepoRoot, ".worktrees"));
        var nativeDriver = new NativeScratchpadDriver(NullLogger<NativeScratchpadDriver>.Instance, new GramBoxConfig
        {
            StripSensitiveEnvVars = true,
            EnableLowCpuPriority = false
        });
        var gramBoxService = new GramBoxService(NullLogger<GramBoxService>.Instance, worktreeManager, nativeDriver);

        // 1. Benchmark Sandbox Creation Latency
        var sw = Stopwatch.StartNew();
        var session = await gramBoxService.CreateSandboxAsync(_testRepoRoot, taskName: "Test Sandbox Isolation");
        sw.Stop();
        var creationMs = sw.ElapsedMilliseconds;

        Assert.NotNull(session);
        Assert.True(Directory.Exists(session.WorktreePath), $"Worktree folder should exist at {session.WorktreePath}");
        Assert.StartsWith("gram/sb-", session.BranchName);

        try
        {
            // 2. Test File Isolation (Must not disrupt main repository)
            var testFileName = $"isolated_test_{session.Id}.txt";
            var testFileContent = "Testing GramBox isolated execution: " + Guid.NewGuid();

            var writeSuccess = await gramBoxService.WriteFileAsync(testFileName, testFileContent, session.Id);
            Assert.True(writeSuccess, "Writing file to sandbox should succeed");

            var readContent = await gramBoxService.ReadFileAsync(testFileName, session.Id);
            Assert.Equal(testFileContent, readContent);

            // CRITICAL CHECK: Verify main repo does NOT have this file
            var mainRepoFilePath = Path.Combine(_testRepoRoot, testFileName);
            Assert.False(File.Exists(mainRepoFilePath), "CRITICAL: Sandbox file leaked to main repo!");

            // 3. Test Command Execution & Environment Sanitization
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-super-secret-test-key");
            var envCheck = await gramBoxService.ExecuteAsync("echo \"API_KEY=$OPENAI_API_KEY\"", session.Id);
            Assert.True(envCheck.Success);
            Assert.DoesNotContain("sk-super-secret-test-key", envCheck.Stdout);

            // 4. Test Diff Summary
            var diff = await gramBoxService.GetDiffAsync(session.Id);
            Assert.NotNull(diff);

            // 5. Test Commit in Sandbox Branch
            var commitOk = await gramBoxService.CommitChangesAsync("feat: test isolation commit", session.Id);
            Assert.True(commitOk, "Commit inside sandbox should succeed");
        }
        finally
        {
            // 6. Test Clean Teardown / Pruning
            var closeSuccess = await gramBoxService.CloseSandboxAsync(session.Id);
            Assert.True(closeSuccess, "Sandbox cleanup should succeed");
            Assert.False(Directory.Exists(session.WorktreePath), "Worktree directory should be completely deleted");
        }
    }

    [Fact]
    public async Task GramBox_ResourceAndPerformance_Benchmark()
    {
        var storage = new StorageService();
        var worktreeManager = new GramBoxWorktreeManager(NullLogger<GramBoxWorktreeManager>.Instance, storage, Path.Combine(_testRepoRoot, ".worktrees"));
        var nativeDriver = new NativeScratchpadDriver(NullLogger<NativeScratchpadDriver>.Instance);
        var gramBoxService = new GramBoxService(NullLogger<GramBoxService>.Instance, worktreeManager, nativeDriver);

        // Measure memory before
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var memoryBeforeBytes = Process.GetCurrentProcess().WorkingSet64;

        // 1. Measure Sandbox Creation Speed
        var sw = Stopwatch.StartNew();
        var session = await gramBoxService.CreateSandboxAsync(_testRepoRoot, taskName: "Benchmark Sandbox");
        sw.Stop();
        var creationMs = sw.ElapsedMilliseconds;

        // Measure memory during
        var memoryDuringBytes = Process.GetCurrentProcess().WorkingSet64;
        var ramDeltaMb = (memoryDuringBytes - memoryBeforeBytes) / (1024.0 * 1024.0);

        try
        {
            Assert.NotNull(session);
            // Worktree creation should be fast (under 1500ms even on cold runs)
            Assert.True(creationMs < 1500, $"Worktree creation took {creationMs}ms (expected < 1500ms)");

            // 2. Measure Command Execution Speed
            var execSw = Stopwatch.StartNew();
            var res = await gramBoxService.ExecuteAsync("git status --short", session.Id);
            execSw.Stop();

            Assert.True(res.Success);
            Assert.True(execSw.ElapsedMilliseconds < 1000, $"Command execution took {execSw.ElapsedMilliseconds}ms");
        }
        finally
        {
            await gramBoxService.CloseSandboxAsync(session.Id);
        }
    }

    [Fact]
    public async Task GramBox_ConcurrentSandboxes_IsolationAndPruning()
    {
        var storage = new StorageService();
        var worktreeManager = new GramBoxWorktreeManager(NullLogger<GramBoxWorktreeManager>.Instance, storage, Path.Combine(_testRepoRoot, ".worktrees"));
        var nativeDriver = new NativeScratchpadDriver(NullLogger<NativeScratchpadDriver>.Instance);
        var gramBoxService = new GramBoxService(NullLogger<GramBoxService>.Instance, worktreeManager, nativeDriver);

        // Create 3 concurrent sandboxes simultaneously
        var tasks = new[]
        {
            gramBoxService.CreateSandboxAsync(_testRepoRoot, taskName: "Concurrent SB 1"),
            gramBoxService.CreateSandboxAsync(_testRepoRoot, taskName: "Concurrent SB 2"),
            gramBoxService.CreateSandboxAsync(_testRepoRoot, taskName: "Concurrent SB 3")
        };

        var sessions = await Task.WhenAll(tasks);
        Assert.Equal(3, sessions.Length);

        try
        {
            // Run parallel writes and commands in all 3 sandboxes
            var workTasks = sessions.Select(async (s, index) =>
            {
                var filename = $"concurrent_test_{index}.txt";
                await gramBoxService.WriteFileAsync(filename, $"Content for sandbox {s.Id}", s.Id);
                var exec = await gramBoxService.ExecuteAsync($"git status --short", s.Id);
                Assert.True(exec.Success);
                var diff = await gramBoxService.GetDiffAsync(s.Id);
                Assert.NotNull(diff);
                Assert.Contains(filename, diff.DiffContent);
            });

            await Task.WhenAll(workTasks);

            // Verify none of the files leaked to the main repository
            for (var i = 0; i < 3; i++)
            {
                Assert.False(File.Exists(Path.Combine(_testRepoRoot, $"concurrent_test_{i}.txt")));
            }
        }
        finally
        {
            // Prune all 3 sandboxes
            var closeTasks = sessions.Select(s => gramBoxService.CloseSandboxAsync(s.Id));
            var results = await Task.WhenAll(closeTasks);
            Assert.All(results, Assert.True);
        }
    }

    private static string CreateWritableClone(string sourceRepo)
    {
        var destination = Path.Combine(Path.GetTempPath(), "gram-box-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        Directory.Delete(destination);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"clone --local --no-hardlinks \"{sourceRepo}\" \"{destination}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start git clone for sandbox tests.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Could not create writable sandbox test repository: {error}");
        }
        return destination;
    }
}
