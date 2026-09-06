using System.Diagnostics;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SandboxEndToEndTests : IAsyncLifetime
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "gram_e2e_repo_" + Guid.NewGuid().ToString("N"));
    private GramBoxService? _service;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_repo);
        await GitAsync(_repo, "git init -b main");
        await GitAsync(_repo, "git config user.email gram-test@example.com");
        await GitAsync(_repo, "git config user.name 'Gram Test'");
        await File.WriteAllTextAsync(Path.Combine(_repo, "README.md"), "base\n");
        await GitAsync(_repo, "git add README.md && git commit -m initial");
        var storage = new StorageService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDir"] = Path.Combine(_repo, ".gram-data") }).Build());
        var manager = new GramBoxWorktreeManager(NullLogger<GramBoxWorktreeManager>.Instance, storage, Path.Combine(_repo, ".worktrees"));
        var scheduler = new SandboxScheduler(storage, NullLogger<SandboxScheduler>.Instance);
        var checkpoints = new SandboxCheckpointService(manager, storage);
        var merge = new SandboxMergeCoordinator(manager);
        var driver = new NativeScratchpadDriver(NullLogger<NativeScratchpadDriver>.Instance, new GramBoxConfig { EnableLowCpuPriority = false });
        _service = new GramBoxService(NullLogger<GramBoxService>.Instance, manager, driver, scheduler, merge, checkpoints);
    }

    [Fact]
    public async Task ParallelSandboxes_CheckpointRollbackAndMergePreviewRemainIsolated()
    {
        var service = _service!;
        var sessions = await Task.WhenAll(
            service.CreateSandboxAsync(_repo, taskName: "prototype-a", runId: "run-a"),
            service.CreateSandboxAsync(_repo, taskName: "prototype-b", runId: "run-b"));
        try
        {
            Assert.Equal(2, sessions.Length);
            Assert.NotEqual(sessions[0].WorktreePath, sessions[1].WorktreePath);
            Assert.Equal(2, sessions.Select(session => session.LeaseId).Distinct().Count());

            Assert.True(await service.WriteFileAsync("a.txt", "A\n", sessions[0].Id));
            Assert.True(await service.WriteFileAsync("b.txt", "B\n", sessions[1].Id));
            Assert.Null(await service.ReadFileAsync("b.txt", sessions[0].Id));

            var checkpoint = await service.CreateCheckpointAsync("before second change", sessions[0].Id);
            Assert.NotNull(checkpoint);
            Assert.True(await service.WriteFileAsync("second.txt", "temporary\n", sessions[0].Id));
            Assert.True(await service.RollbackAsync(checkpoint!.Id, sessions[0].Id));
            Assert.Equal("A\n", await service.ReadFileAsync("a.txt", sessions[0].Id));
            Assert.Null(await service.ReadFileAsync("second.txt", sessions[0].Id));

            Assert.True((await service.ExecuteAsync("git add -A && git commit -m prototype-a", sessions[0].Id)).Success);
            var preview = await service.PreviewMergeAsync(sessions[0].Id, "main");
            Assert.True(preview.CanMergeCleanly);
            Assert.Contains("a.txt", preview.ChangedFiles);
        }
        finally
        {
            foreach (var session in sessions) await service.CloseSandboxAsync(session.Id);
        }
    }

    private static async Task GitAsync(string directory, string command)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c {command}")
            : new ProcessStartInfo("/bin/bash", $"-c \"{command.Replace("\"", "\\\"")}\"");
        psi.WorkingDirectory = directory;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
    }

    public Task DisposeAsync()
    {
        try { if (Directory.Exists(_repo)) Directory.Delete(_repo, true); } catch { }
        return Task.CompletedTask;
    }
}
