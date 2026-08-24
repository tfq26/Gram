using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gram.Blazor.Shared;

public enum GitGramTaskType
{
    StatusInspection,
    BranchCreate,
    SemanticCommit,
    StageAll,
    StageFile,
    DiscardFile,
    AcceptArtifact,
    DiffAnalysis,
    ConflictCheck
}

public enum GitGramTaskStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    RejectedSafety
}

public sealed class GitGramTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public GitGramTaskType Type { get; set; }
    public string Description { get; set; } = "";
    public string? TargetFilePath { get; set; }
    public string? Payload { get; set; }
    public GitGramTaskStatus Status { get; set; } = GitGramTaskStatus.Queued;
    public string? ResultOutput { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public sealed class GitGramLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Command { get; set; } = "";
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public bool IsSuccess => ExitCode == 0;
    public double ElapsedMs { get; set; }
}

public sealed class GitGramRepoState
{
    public string RepoPath { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string CurrentBranch { get; set; } = "main";
    public bool IsClean { get; set; } = true;
    public int ModifiedFileCount { get; set; }
    public int StagedFileCount { get; set; }
    public int UntrackedFileCount { get; set; }
    public string LastCommitHash { get; set; } = "";
    public string LastCommitMessage { get; set; } = "";
    public DateTime LastCheckedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class GitGramService
{
    private readonly ISonnerService _sonner;
    private readonly object _lock = new();
    private readonly Queue<GitGramTask> _taskQueue = new();
    private bool _isProcessingQueue = false;

    public GitGramRepoState ActiveRepoState { get; private set; } = new();
    public List<GitGramTask> TaskHistory { get; private set; } = new();
    public List<GitGramLogEntry> AuditLogs { get; private set; } = new();

    public string AssignedModelId { get; set; } = "google/gemini-2.5-flash";
    public bool IsAutonomousCommitEnabled { get; set; } = true;
    public bool EnforceStrictSafety { get; set; } = true;

    public event Action? OnStateChanged;

    public GitGramService(ISonnerService sonner)
    {
        _sonner = sonner;
    }

    public void SetActiveWorkspace(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
            return;

        var fullPath = Path.GetFullPath(workspacePath);
        if (ActiveRepoState.RepoPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
            return;

        ActiveRepoState = new GitGramRepoState
        {
            RepoPath = fullPath,
            RepoName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        };

        EnqueueTask(new GitGramTask
        {
            Type = GitGramTaskType.StatusInspection,
            Description = $"Initial inspection of repository '{ActiveRepoState.RepoName}'"
        });
    }

    public GitGramTask EnqueueTask(GitGramTask task)
    {
        lock (_lock)
        {
            _taskQueue.Enqueue(task);
            TaskHistory.Insert(0, task);
            if (TaskHistory.Count > 40)
            {
                TaskHistory = TaskHistory.Take(40).ToList();
            }
        }

        OnStateChanged?.Invoke();
        _ = ProcessQueueAsync();
        return task;
    }

    public async Task<bool> ApplyArtifactToWorkspaceAsync(string relativeFilePath, string content, string workspacePath)
    {
        try
        {
            var targetFull = Path.GetFullPath(Path.Combine(workspacePath, relativeFilePath));
            var dir = Path.GetDirectoryName(targetFull);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(targetFull, content);

            EnqueueTask(new GitGramTask
            {
                Type = GitGramTaskType.StatusInspection,
                TargetFilePath = relativeFilePath,
                Description = $"Inspect working tree after writing '{relativeFilePath}'"
            });

            _sonner.Success($"Applied '{relativeFilePath}' to workspace.", "Git Gram File Sync");
            return true;
        }
        catch (Exception ex)
        {
            _sonner.Error($"Failed to apply artifact: {ex.Message}", "Git Gram File Error");
            return false;
        }
    }

    public async Task<bool> DiscardFileChangesAsync(string relativeFilePath, string workspacePath)
    {
        var task = EnqueueTask(new GitGramTask
        {
            Type = GitGramTaskType.DiscardFile,
            TargetFilePath = relativeFilePath,
            Description = $"Discard unstaged changes in '{relativeFilePath}'"
        });

        await WaitForTaskAsync(task.Id);
        return task.Status == GitGramTaskStatus.Completed;
    }

    public async Task<bool> StageFileAsync(string relativeFilePath, string workspacePath)
    {
        var task = EnqueueTask(new GitGramTask
        {
            Type = GitGramTaskType.StageFile,
            TargetFilePath = relativeFilePath,
            Description = $"Stage file '{relativeFilePath}'"
        });

        await WaitForTaskAsync(task.Id);
        return task.Status == GitGramTaskStatus.Completed;
    }

    public async Task<bool> CommitChangesAsync(string commitMessage, string workspacePath)
    {
        var task = EnqueueTask(new GitGramTask
        {
            Type = GitGramTaskType.SemanticCommit,
            Payload = commitMessage,
            Description = $"Commit changes: {commitMessage}"
        });

        await WaitForTaskAsync(task.Id);
        return task.Status == GitGramTaskStatus.Completed;
    }

    private async Task WaitForTaskAsync(string taskId)
    {
        var maxWait = TimeSpan.FromSeconds(8);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < maxWait)
        {
            var t = TaskHistory.FirstOrDefault(x => x.Id == taskId);
            if (t != null && t.Status != GitGramTaskStatus.Queued && t.Status != GitGramTaskStatus.Running)
            {
                break;
            }
            await Task.Delay(100);
        }
    }

    private async Task ProcessQueueAsync()
    {
        lock (_lock)
        {
            if (_isProcessingQueue) return;
            _isProcessingQueue = true;
        }

        try
        {
            while (true)
            {
                GitGramTask? task = null;
                lock (_lock)
                {
                    if (_taskQueue.Count == 0)
                    {
                        _isProcessingQueue = false;
                        break;
                    }
                    task = _taskQueue.Dequeue();
                }

                if (task != null)
                {
                    await ExecuteGitGramTaskAsync(task);
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                _isProcessingQueue = false;
            }
            OnStateChanged?.Invoke();
        }
    }

    private async Task ExecuteGitGramTaskAsync(GitGramTask task)
    {
        task.Status = GitGramTaskStatus.Running;
        OnStateChanged?.Invoke();

        var repoDir = string.IsNullOrWhiteSpace(ActiveRepoState.RepoPath) ? Environment.CurrentDirectory : ActiveRepoState.RepoPath;

        try
        {
            switch (task.Type)
            {
                case GitGramTaskType.StatusInspection:
                {
                    var statusRes = await RunRawGitAsync("status --porcelain -b", repoDir);
                    var logRes = await RunRawGitAsync("log -n 1 --pretty=format:\"%h|%s\"", repoDir);

                    ParsePorcelainStatus(statusRes.Output);

                    if (logRes.IsSuccess && !string.IsNullOrWhiteSpace(logRes.Output))
                    {
                        var parts = logRes.Output.Split('|');
                        if (parts.Length > 0) ActiveRepoState.LastCommitHash = parts[0];
                        if (parts.Length > 1) ActiveRepoState.LastCommitMessage = parts[1];
                    }

                    task.Status = GitGramTaskStatus.Completed;
                    task.ResultOutput = $"Status inspected. Branch: {ActiveRepoState.CurrentBranch}, Modified: {ActiveRepoState.ModifiedFileCount}, Clean: {ActiveRepoState.IsClean}";
                    break;
                }

                case GitGramTaskType.BranchCreate:
                {
                    var branchName = string.IsNullOrWhiteSpace(task.Payload) ? "feature/gram-update" : SanitizeBranchName(task.Payload);
                    var res = await RunRawGitAsync($"checkout -b \"{branchName}\"", repoDir);
                    if (res.IsSuccess)
                    {
                        ActiveRepoState.CurrentBranch = branchName;
                        task.Status = GitGramTaskStatus.Completed;
                        task.ResultOutput = $"Created and switched to branch '{branchName}'";
                        _sonner.Success($"Switched to new branch: {branchName}", "Git Gram Branch");
                    }
                    else
                    {
                        task.Status = GitGramTaskStatus.Failed;
                        task.ResultOutput = res.Output;
                        _sonner.Error($"Failed to create branch '{branchName}': {res.Output}", "Git Gram Error");
                    }
                    break;
                }

                case GitGramTaskType.StageAll:
                {
                    var res = await RunRawGitAsync("add -A", repoDir);
                    task.Status = res.IsSuccess ? GitGramTaskStatus.Completed : GitGramTaskStatus.Failed;
                    task.ResultOutput = res.Output;
                    await RefreshStatusAsync(repoDir);
                    break;
                }

                case GitGramTaskType.StageFile:
                {
                    if (string.IsNullOrWhiteSpace(task.TargetFilePath))
                    {
                        task.Status = GitGramTaskStatus.Failed;
                        task.ResultOutput = "Target file path required for staging.";
                        break;
                    }
                    var res = await RunRawGitAsync($"add \"{task.TargetFilePath}\"", repoDir);
                    task.Status = res.IsSuccess ? GitGramTaskStatus.Completed : GitGramTaskStatus.Failed;
                    task.ResultOutput = res.Output;
                    await RefreshStatusAsync(repoDir);
                    break;
                }

                case GitGramTaskType.DiscardFile:
                {
                    if (string.IsNullOrWhiteSpace(task.TargetFilePath))
                    {
                        task.Status = GitGramTaskStatus.Failed;
                        task.ResultOutput = "Target file path required for discarding.";
                        break;
                    }

                    var res = await RunRawGitAsync($"restore \"{task.TargetFilePath}\"", repoDir);
                    task.Status = res.IsSuccess ? GitGramTaskStatus.Completed : GitGramTaskStatus.Failed;
                    task.ResultOutput = res.Output;
                    if (res.IsSuccess)
                    {
                        _sonner.Info($"Discarded changes in {task.TargetFilePath}", "Git Gram Discard");
                    }
                    await RefreshStatusAsync(repoDir);
                    break;
                }

                case GitGramTaskType.SemanticCommit:
                {
                    var msg = string.IsNullOrWhiteSpace(task.Payload) ? "chore: autonomous repository update" : task.Payload.Trim();
                    await RunRawGitAsync("add -A", repoDir);
                    var cleanMsg = msg.Replace("\"", "\\\"");
                    var res = await RunRawGitAsync($"commit -m \"{cleanMsg}\"", repoDir);

                    if (res.IsSuccess)
                    {
                        task.Status = GitGramTaskStatus.Completed;
                        task.ResultOutput = res.Output;
                        _sonner.Success($"Committed: {msg}", "Git Gram Semantic Commit");
                    }
                    else
                    {
                        task.Status = GitGramTaskStatus.Failed;
                        task.ResultOutput = res.Output;
                        _sonner.Warning($"Commit skipped or failed: {res.Output}", "Git Gram VCS");
                    }
                    await RefreshStatusAsync(repoDir);
                    break;
                }

                case GitGramTaskType.DiffAnalysis:
                {
                    var res = await RunRawGitAsync("diff HEAD", repoDir);
                    task.Status = res.IsSuccess ? GitGramTaskStatus.Completed : GitGramTaskStatus.Failed;
                    task.ResultOutput = res.Output;
                    break;
                }

                default:
                    task.Status = GitGramTaskStatus.Completed;
                    break;
            }
        }
        catch (Exception ex)
        {
            task.Status = GitGramTaskStatus.Failed;
            task.ResultOutput = $"Execution exception: {ex.Message}";
        }
        finally
        {
            task.CompletedAt = DateTime.UtcNow;
            OnStateChanged?.Invoke();
        }
    }

    private async Task RefreshStatusAsync(string repoDir)
    {
        var statusRes = await RunRawGitAsync("status --porcelain -b", repoDir);
        ParsePorcelainStatus(statusRes.Output);
    }

    private void ParsePorcelainStatus(string porcelainOutput)
    {
        if (string.IsNullOrWhiteSpace(porcelainOutput))
        {
            ActiveRepoState.IsClean = true;
            ActiveRepoState.ModifiedFileCount = 0;
            ActiveRepoState.StagedFileCount = 0;
            ActiveRepoState.UntrackedFileCount = 0;
            ActiveRepoState.LastCheckedUtc = DateTime.UtcNow;
            return;
        }

        var lines = porcelainOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int modified = 0;
        int staged = 0;
        int untracked = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("##"))
            {
                var branchMatch = Regex.Match(line, @"^##\s+([^\s\.]+)");
                if (branchMatch.Success)
                {
                    ActiveRepoState.CurrentBranch = branchMatch.Groups[1].Value;
                }
                continue;
            }

            if (line.Length >= 2)
            {
                char x = line[0];
                char y = line[1];

                if (x == '?' && y == '?') untracked++;
                else
                {
                    if (x != ' ' && x != '?') staged++;
                    if (y != ' ') modified++;
                }
            }
        }

        ActiveRepoState.ModifiedFileCount = modified;
        ActiveRepoState.StagedFileCount = staged;
        ActiveRepoState.UntrackedFileCount = untracked;
        ActiveRepoState.IsClean = (modified + staged + untracked) == 0;
        ActiveRepoState.LastCheckedUtc = DateTime.UtcNow;
    }

    private async Task<GitGramLogEntry> RunRawGitAsync(string arguments, string workingDirectory)
    {
        if (EnforceStrictSafety)
        {
            var lower = arguments.ToLowerInvariant();
            if (lower.Contains("reset --hard") || lower.Contains("clean -fd") || lower.Contains("push --force") || lower.Contains("branch -d"))
            {
                var blockedLog = new GitGramLogEntry
                {
                    Command = $"git {arguments}",
                    ExitCode = -1,
                    Output = "Blocked by Git Gram Strict Safety Guardrails. Destructive history modifications require explicit user confirmation.",
                    ElapsedMs = 0
                };
                AuditLogs.Insert(0, blockedLog);
                return blockedLog;
            }
        }

        var sw = Stopwatch.StartNew();
        var entry = new GitGramLogEntry
        {
            Command = $"git {arguments}"
        };

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            var outText = await proc.StandardOutput.ReadToEndAsync();
            var errText = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            sw.Stop();
            entry.ExitCode = proc.ExitCode;
            entry.Output = string.IsNullOrWhiteSpace(outText) ? errText : outText;
            entry.ElapsedMs = sw.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex)
        {
            sw.Stop();
            entry.ExitCode = -1;
            entry.Output = $"Process error: {ex.Message}";
            entry.ElapsedMs = sw.Elapsed.TotalMilliseconds;
        }

        AuditLogs.Insert(0, entry);
        if (AuditLogs.Count > 100)
        {
            AuditLogs = AuditLogs.Take(100).ToList();
        }

        return entry;
    }

    private static string SanitizeBranchName(string name)
    {
        var sanitized = Regex.Replace(name.Trim(), @"[^a-zA-Z0-9_\-\/]", "-");
        sanitized = Regex.Replace(sanitized, @"-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "feature/gram-update" : sanitized;
    }
}
