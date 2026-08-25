using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed class GramBoxWorktreeManager
{
    private readonly ILogger<GramBoxWorktreeManager> _logger;
    private readonly StorageService _storageService;
    private readonly string _worktreesRoot;

    public string WorktreesRoot => _worktreesRoot;

    public GramBoxWorktreeManager(ILogger<GramBoxWorktreeManager> logger, StorageService storageService)
    {
        _logger = logger;
        _storageService = storageService;

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _worktreesRoot = Path.Combine(homeDir, ".gram", "box", "worktrees");
        
        try
        {
            Directory.CreateDirectory(_worktreesRoot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create worktrees root directory: {Path}", _worktreesRoot);
        }
    }

    public async Task<GramBoxSandboxSession> CreateWorktreeSessionAsync(
        string baseRepoPath, 
        string? customBranchName = null, 
        string? taskDescription = null,
        CancellationToken ct = default)
    {
        var id = "sb-" + Guid.NewGuid().ToString("N")[..8];
        var branch = string.IsNullOrWhiteSpace(customBranchName) 
            ? $"gram/{id}" 
            : customBranchName.Trim().Replace(" ", "-").ToLowerInvariant();

        var worktreePath = Path.Combine(_worktreesRoot, id);

        // Ensure base directory exists
        Directory.CreateDirectory(_worktreesRoot);

        // Execute: git worktree add <worktreePath> -b <branch>
        var addCmd = $"worktree add \"{worktreePath}\" -b \"{branch}\"";
        var (exitCode, stdout, stderr) = await RunGitAsync(baseRepoPath, addCmd, ct);

        if (exitCode != 0)
        {
            // If branch already exists, try checking it out without -b
            var fallbackCmd = $"worktree add \"{worktreePath}\" \"{branch}\"";
            var (fallbackExit, _, fallbackErr) = await RunGitAsync(baseRepoPath, fallbackCmd, ct);
            if (fallbackExit != 0)
            {
                throw new InvalidOperationException($"Failed to create git worktree: {stderr} | {fallbackErr}");
            }
        }

        var session = new GramBoxSandboxSession
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(taskDescription) ? $"Sandbox {id}" : taskDescription,
            BranchName = branch,
            WorktreePath = worktreePath,
            BaseRepoPath = baseRepoPath,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            Status = SandboxStatus.Ready,
            DriverType = GramBoxEngineType.NativeWorktree
        };

        _logger.LogInformation("Created Gram Box worktree {Id} on branch {Branch} at {Path}", id, branch, worktreePath);
        return session;
    }

    public async Task<SandboxDiffSummary> GetWorktreeDiffAsync(GramBoxSandboxSession session, CancellationToken ct = default)
    {
        var diffSummary = new SandboxDiffSummary
        {
            SandboxId = session.Id,
            BranchName = session.BranchName
        };

        if (!Directory.Exists(session.WorktreePath)) return diffSummary;

        // 1. Get git diff stat
        var (statExit, statOut, _) = await RunGitAsync(session.WorktreePath, "diff --stat HEAD~1 2>/dev/null || git diff --stat", ct);
        // 2. Get full git diff
        var (diffExit, diffOut, _) = await RunGitAsync(session.WorktreePath, "diff HEAD~1 2>/dev/null || git diff", ct);

        diffSummary.DiffContent = diffExit == 0 ? diffOut : "";

        if (statExit == 0 && !string.IsNullOrWhiteSpace(statOut))
        {
            var lines = statOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var summaryLine = lines.LastOrDefault() ?? "";
            
            // Parse summary line (e.g. " 3 files changed, 45 insertions(+), 12 deletions(-)")
            if (summaryLine.Contains("changed"))
            {
                var parts = summaryLine.Split(',');
                foreach (var part in parts)
                {
                    var p = part.Trim();
                    if (p.Contains("file"))
                    {
                        var numStr = new string(p.TakeWhile(char.IsDigit).ToArray());
                        if (int.TryParse(numStr, out var files)) diffSummary.FilesChanged = files;
                    }
                    else if (p.Contains("insertion"))
                    {
                        var numStr = new string(p.TakeWhile(char.IsDigit).ToArray());
                        if (int.TryParse(numStr, out var ins)) diffSummary.Insertions = ins;
                    }
                    else if (p.Contains("deletion"))
                    {
                        var numStr = new string(p.TakeWhile(char.IsDigit).ToArray());
                        if (int.TryParse(numStr, out var del)) diffSummary.Deletions = del;
                    }
                }
            }
        }

        return diffSummary;
    }

    public async Task<bool> CommitWorktreeChangesAsync(GramBoxSandboxSession session, string commitMessage, CancellationToken ct = default)
    {
        if (!Directory.Exists(session.WorktreePath)) return false;

        // git add -A
        var (addExit, _, _) = await RunGitAsync(session.WorktreePath, "add -A", ct);
        if (addExit != 0) return false;

        // git commit -m "..."
        var safeMsg = commitMessage.Replace("\"", "\\\"");
        var (commitExit, _, _) = await RunGitAsync(session.WorktreePath, $"commit -m \"{safeMsg}\"", ct);

        return commitExit == 0;
    }

    public async Task<bool> RemoveWorktreeSessionAsync(GramBoxSandboxSession session, bool force = true, CancellationToken ct = default)
    {
        try
        {
            if (Directory.Exists(session.WorktreePath))
            {
                var flag = force ? "--force" : "";
                await RunGitAsync(session.BaseRepoPath, $"worktree remove {flag} \"{session.WorktreePath}\"", ct);
                
                // Fallback delete if still exists
                if (Directory.Exists(session.WorktreePath))
                {
                    Directory.Delete(session.WorktreePath, recursive: true);
                }
            }

            await RunGitAsync(session.BaseRepoPath, "worktree prune", ct);
            session.Status = SandboxStatus.Disposed;
            _logger.LogInformation("Removed Gram Box worktree {Id} at {Path}", session.Id, session.WorktreePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove worktree {Id}: {Message}", session.Id, ex.Message);
            return false;
        }
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunGitAsync(string workingDir, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = Directory.Exists(workingDir) ? workingDir : Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            await process.WaitForExitAsync(linkedCts.Token);
            return (process.ExitCode, stdout.ToString().TrimEnd(), stderr.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return (-1, stdout.ToString(), ex.Message);
        }
    }
}
