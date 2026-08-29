using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed record SandboxBenchmarkReport(
    int SandboxCount,
    long CreationMilliseconds,
    long CommandMilliseconds,
    long RepositoryBytes,
    long WorktreeBytes,
    long ProcessWorkingSetBeforeBytes,
    long ProcessWorkingSetAfterBytes,
    int SuccessfulCommands,
    string EnforcementMode,
    string? Error = null);

public sealed class SandboxBenchmarkService
{
    private readonly StorageService _storage;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IPlatformSandboxController _platformController;

    public SandboxBenchmarkService(StorageService storage, ILoggerFactory loggerFactory, IPlatformSandboxController platformController)
    {
        _storage = storage;
        _loggerFactory = loggerFactory;
        _platformController = platformController;
    }

    public async Task<SandboxBenchmarkReport> RunAsync(int sandboxCount = 10, CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(Path.GetTempPath(), "gram-sandbox-benchmark-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(root, "repo");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(repo);
        var before = Process.GetCurrentProcess().WorkingSet64;
        try
        {
            await RunGitAsync(repo, "git init -b main");
            await RunGitAsync(repo, "git config user.email benchmark@gram.local");
            await RunGitAsync(repo, "git config user.name 'Gram Benchmark'");
            for (var index = 0; index < 20; index++) await File.WriteAllTextAsync(Path.Combine(repo, $"sample-{index}.txt"), new string('x', 4096), cancellationToken);
            await RunGitAsync(repo, "git add -A && git commit -m benchmark");

            var testStorage = new StorageService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDir"] = data }).Build());
            var manager = new GramBoxWorktreeManager(_loggerFactory.CreateLogger<GramBoxWorktreeManager>(), testStorage, Path.Combine(root, "worktrees"));
            var scheduler = new SandboxScheduler(testStorage, _loggerFactory.CreateLogger<SandboxScheduler>());
            var driver = new NativeScratchpadDriver(_loggerFactory.CreateLogger<NativeScratchpadDriver>(), new GramBoxConfig { EnableLowCpuPriority = false }, _platformController, scheduler);
            var service = new GramBoxService(_loggerFactory.CreateLogger<GramBoxService>(), manager, driver, scheduler, new SandboxMergeCoordinator(manager), new SandboxCheckpointService(manager, testStorage));

            var creationTimer = Stopwatch.StartNew();
            var sessions = await Task.WhenAll(Enumerable.Range(1, sandboxCount).Select(index => service.CreateSandboxAsync(repo, taskName: $"Benchmark {index}", runId: $"benchmark-{index}")));
            creationTimer.Stop();
            var repoBytes = DirectoryBytes(repo);
            var worktreeBytes = sessions.Sum(session => DirectoryBytes(session.WorktreePath));

            var commandTimer = Stopwatch.StartNew();
            var commandResults = await Task.WhenAll(sessions.Select(session => service.ExecuteAsync("git status --short", session.Id, timeoutSeconds: 30, ct: cancellationToken)));
            commandTimer.Stop();
            var after = Process.GetCurrentProcess().WorkingSet64;
            foreach (var session in sessions) await service.CloseSandboxAsync(session.Id, ct: cancellationToken);
            return new SandboxBenchmarkReport(sessions.Length, creationTimer.ElapsedMilliseconds, commandTimer.ElapsedMilliseconds, repoBytes, worktreeBytes, before, after, commandResults.Count(result => result.Success), _platformController.Capabilities.EnforcementMode);
        }
        catch (Exception ex)
        {
            return new SandboxBenchmarkReport(0, 0, 0, 0, 0, before, Process.GetCurrentProcess().WorkingSet64, 0, _platformController.Capabilities.EnforcementMode, ex.Message);
        }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }
    }

    private static long DirectoryBytes(string path) => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) : 0;

    private static async Task RunGitAsync(string directory, string command)
    {
        var psi = new ProcessStartInfo("/bin/bash", $"-c \"{command.Replace("\"", "\\\"")}\"") { WorkingDirectory = directory, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(await process.StandardError.ReadToEndAsync());
    }
}
