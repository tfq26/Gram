using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public bool BlockedByPolicy { get; set; }
    public double ElapsedMs { get; set; }
}

public sealed record GitGramChangedFile(
    string RelativePath,
    string Status,
    bool IsStaged
);

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
    public List<GitGramChangedFile> ChangedFiles { get; set; } = new();
    public DateTime LastCheckedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class GitGramActionResult
{
    public bool Success { get; init; }
    public string Output { get; init; } = "";
    public string? ErrorCode { get; init; }
    public JsonObject Data { get; init; } = new();
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
            if (!TryResolveWorkspacePath(workspacePath, relativeFilePath, out var targetFull))
            {
                _sonner.Error($"Refused to write outside the active workspace: {relativeFilePath}", "Git Gram File Safety");
                return false;
            }

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
        if (!TryResolveWorkspacePath(workspacePath, relativeFilePath, out _))
        {
            _sonner.Error($"Refused to discard outside the active workspace: {relativeFilePath}", "Git Gram File Safety");
            return false;
        }

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
        if (!TryResolveWorkspacePath(workspacePath, relativeFilePath, out _))
        {
            _sonner.Error($"Refused to stage outside the active workspace: {relativeFilePath}", "Git Gram File Safety");
            return false;
        }

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

    public async Task<GitGramActionResult> ExecuteControlledActionAsync(
        string action,
        string? payload,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(workspacePath) && Directory.Exists(workspacePath))
        {
            SetActiveWorkspace(workspacePath);
        }

        var normalizedAction = (action ?? "status").Trim().ToLowerInvariant();
        switch (normalizedAction)
        {
            case "status":
            {
                var task = EnqueueTask(new GitGramTask
                {
                    Type = GitGramTaskType.StatusInspection,
                    Description = "Inspect repository status through Git Gram"
                });
                await WaitForTaskAsync(task.Id, cancellationToken);
                return TaskResult(task, new JsonObject
                {
                    ["branch"] = ActiveRepoState.CurrentBranch,
                    ["isClean"] = ActiveRepoState.IsClean,
                    ["modifiedFiles"] = ActiveRepoState.ModifiedFileCount,
                    ["stagedFiles"] = ActiveRepoState.StagedFileCount,
                    ["untrackedFiles"] = ActiveRepoState.UntrackedFileCount
                });
            }

            case "stage_all":
            {
                var task = EnqueueTask(new GitGramTask
                {
                    Type = GitGramTaskType.StageAll,
                    Description = "Stage all changes through Git Gram"
                });
                await WaitForTaskAsync(task.Id, cancellationToken);
                return TaskResult(task);
            }

            case "stage_file":
                return await ExecuteExistingActionAsync(StageFileAsync(payload ?? "", workspacePath), normalizedAction, cancellationToken);

            case "discard_file":
                return await ExecuteExistingActionAsync(DiscardFileChangesAsync(payload ?? "", workspacePath), normalizedAction, cancellationToken);

            case "commit":
            {
                if (!IsAutonomousCommitEnabled)
                {
                    return new GitGramActionResult
                    {
                        ErrorCode = "autonomous_commit_disabled",
                        Output = "Git Gram autonomous commits are disabled by policy."
                    };
                }

                return await ExecuteExistingActionAsync(CommitChangesAsync(payload ?? "chore: autonomous repository update", workspacePath), normalizedAction, cancellationToken);
            }

            case "create_branch":
            {
                var task = EnqueueTask(new GitGramTask
                {
                    Type = GitGramTaskType.BranchCreate,
                    Payload = payload,
                    Description = $"Create branch '{payload}' through Git Gram"
                });
                await WaitForTaskAsync(task.Id, cancellationToken);
                return TaskResult(task);
            }

            case "branch":
                return new GitGramActionResult
                {
                    Success = true,
                    Output = $"Current branch: {ActiveRepoState.CurrentBranch}",
                    Data = new JsonObject { ["currentBranch"] = ActiveRepoState.CurrentBranch }
                };

            case "log":
                return new GitGramActionResult
                {
                    Success = true,
                    Output = string.Join(Environment.NewLine, AuditLogs.Take(10).Select(x => $"{x.Command} ({x.ElapsedMs:0}ms)")),
                    Data = new JsonObject { ["entries"] = AuditLogs.Take(10).Count() }
                };

            case "diff":
            {
                var task = EnqueueTask(new GitGramTask
                {
                    Type = GitGramTaskType.DiffAnalysis,
                    Description = "Analyze repository diff through Git Gram"
                });
                await WaitForTaskAsync(task.Id, cancellationToken);
                return TaskResult(task);
            }

            default:
                return new GitGramActionResult
                {
                    ErrorCode = "unsupported_git_action",
                    Output = $"Git Gram does not support the action '{action}'."
                };
        }
    }

    private static async Task<GitGramActionResult> ExecuteExistingActionAsync(Task<bool> operation, string action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var success = await operation;
        return new GitGramActionResult
        {
            Success = success,
            Output = success ? $"Git Gram completed '{action}'." : $"Git Gram failed '{action}'.",
            ErrorCode = success ? null : "git_operation_failed"
        };
    }

    private static GitGramActionResult TaskResult(GitGramTask task, JsonObject? data = null) => new()
    {
        Success = task.Status == GitGramTaskStatus.Completed,
        Output = task.ResultOutput ?? $"Git Gram task {task.Status}.",
        ErrorCode = task.Status == GitGramTaskStatus.Completed ? null : $"git_task_{task.Status.ToString().ToLowerInvariant()}",
        Data = data ?? new JsonObject { ["taskId"] = task.Id, ["status"] = task.Status.ToString() }
    };

    private async Task WaitForTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var maxWait = TimeSpan.FromSeconds(8);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < maxWait)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                    var statusRes = await RunGitAsync(repoDir, "status", "--porcelain", "-b");
                    var logRes = await RunGitAsync(repoDir, "log", "-n", "1", "--pretty=format:%h|%s");

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
                    var res = await RunGitAsync(repoDir, "checkout", "-b", branchName);
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
                    var res = await RunGitAsync(repoDir, "add", "-A");
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
                    var res = await RunGitAsync(repoDir, "add", "--", task.TargetFilePath);
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

                    var res = await RunGitAsync(repoDir, "restore", "--", task.TargetFilePath);
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
                    await RunGitAsync(repoDir, "add", "-A");
                    var res = await RunGitAsync(repoDir, "commit", "-m", msg);

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
                    var res = await RunGitAsync(repoDir, "diff", "HEAD");
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
        var statusRes = await RunGitAsync(repoDir, "status", "--porcelain", "-b");
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
            ActiveRepoState.ChangedFiles = new();
            ActiveRepoState.LastCheckedUtc = DateTime.UtcNow;
            return;
        }

        var lines = porcelainOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int modified = 0;
        int staged = 0;
        int untracked = 0;
        var changedList = new List<GitGramChangedFile>();

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

            if (line.Length >= 4)
            {
                char x = line[0];
                char y = line[1];
                var statusStr = line[..2].Trim();
                var rawPath = line[3..].Trim().Trim('"');

                bool isUntracked = x == '?' && y == '?';
                bool isStagedFile = x != ' ' && x != '?';
                bool isModifiedFile = y != ' ';

                if (isUntracked) untracked++;
                else
                {
                    if (isStagedFile) staged++;
                    if (isModifiedFile) modified++;
                }

                changedList.Add(new GitGramChangedFile(
                    RelativePath: rawPath,
                    Status: isUntracked ? "??" : (string.IsNullOrWhiteSpace(statusStr) ? "M" : statusStr),
                    IsStaged: isStagedFile
                ));
            }
        }

        ActiveRepoState.ModifiedFileCount = modified;
        ActiveRepoState.StagedFileCount = staged;
        ActiveRepoState.UntrackedFileCount = untracked;
        ActiveRepoState.ChangedFiles = changedList;
        ActiveRepoState.IsClean = (modified + staged + untracked) == 0;
        ActiveRepoState.LastCheckedUtc = DateTime.UtcNow;
    }

    public async Task RefreshAsync(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath)) return;
        SetActiveWorkspace(workspacePath);
        await RefreshStatusAsync(workspacePath);
        OnStateChanged?.Invoke();
    }

    public async Task<string> GetFileDiffAsync(string workspacePath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath) || string.IsNullOrWhiteSpace(relativePath))
            return "";

        var res = await RunGitAsync(workspacePath, "diff", "HEAD", "--", relativePath);
        if (!res.IsSuccess || string.IsNullOrWhiteSpace(res.Output))
        {
            res = await RunGitAsync(workspacePath, "diff", "--", relativePath);
        }
        return res.Output ?? "";
    }

    public async Task<bool> UnstageFileAsync(string relativePath, string workspacePath)
    {
        if (!TryResolveWorkspacePath(workspacePath, relativePath, out _))
            return false;

        var res = await RunGitAsync(workspacePath, "restore", "--staged", "--", relativePath);
        await RefreshStatusAsync(workspacePath);
        return res.IsSuccess;
    }

    public async Task<bool> StashChangesAsync(string workspacePath, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath)) return false;

        var args = string.IsNullOrWhiteSpace(message)
            ? new[] { "stash", "push", "-u" }
            : new[] { "stash", "push", "-u", "-m", message };

        var res = await RunGitAsync(workspacePath, args);
        await RefreshStatusAsync(workspacePath);
        return res.IsSuccess;
    }

    public async Task<bool> PopStashAsync(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath)) return false;

        var res = await RunGitAsync(workspacePath, "stash", "pop");
        await RefreshStatusAsync(workspacePath);
        return res.IsSuccess;
    }

    private async Task<GitGramLogEntry> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var command = $"git {string.Join(' ', arguments.Select(QuoteForLog))}";
        if (EnforceStrictSafety && IsBlockedGitOperation(arguments))
        {
            var blockedLog = new GitGramLogEntry
            {
                Command = command,
                ExitCode = -1,
                BlockedByPolicy = true,
                Output = "Blocked by Git Gram Strict Safety Guardrails. Destructive history modifications require explicit user confirmation.",
                ElapsedMs = 0
            };
            AuditLogs.Insert(0, blockedLog);
            return blockedLog;
        }

        var sw = Stopwatch.StartNew();
        var entry = new GitGramLogEntry
        {
            Command = command
        };

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
            {
                proc.StartInfo.ArgumentList.Add(argument);
            }

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

    private static bool IsBlockedGitOperation(IReadOnlyList<string> arguments)
    {
        var verb = arguments.FirstOrDefault()?.ToLowerInvariant() ?? "";
        var lower = arguments.Select(x => x.ToLowerInvariant()).ToArray();

        return (verb == "reset" && lower.Contains("--hard"))
            || (verb == "clean" && lower.Any(x => x.Contains("-f") && x.Contains("d")))
            || (verb == "push" && lower.Any(x => x is "--force" or "-f" or "--force-with-lease"))
            || (verb == "branch" && lower.Any(x => x is "-d" or "-D" or "--delete"));
    }

    private static string QuoteForLog(string value)
    {
        return value.IndexOfAny([' ', '\"', '\\']) >= 0 ? $"\"{value.Replace("\"", "\\\"")}\"" : value;
    }

    private static bool TryResolveWorkspacePath(string workspacePath, string relativePath, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(workspacePath) || string.IsNullOrWhiteSpace(relativePath))
            return false;

        try
        {
            var fullRoot = Path.GetFullPath(workspacePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            fullPath = Path.GetFullPath(Path.Combine(workspacePath, relativePath));
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            fullPath = "";
            return false;
        }
    }

    private static string SanitizeBranchName(string name)
    {
        var sanitized = Regex.Replace(name.Trim(), @"[^a-zA-Z0-9_\-\/]", "-");
        sanitized = Regex.Replace(sanitized, @"-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "feature/gram-update" : sanitized;
    }
}
