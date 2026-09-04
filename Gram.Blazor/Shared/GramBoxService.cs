using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public interface IGramBoxService
{
    IReadOnlyList<GramBoxSandboxSession> ActiveSessions { get; }
    GramBoxSandboxSession? ActiveSession { get; }
    GramBoxEngineType ActiveEngine { get; }
    bool IsReady { get; }
    string? StatusMessage { get; }
    event Action? OnStatusChanged;
    event Action<GramBoxSandboxSession, SandboxMajorState>? OnSandboxStateChanged;

    void SubscribeChat(string sandboxId, string chatId);
    void UnsubscribeChat(string sandboxId, string chatId);
    IReadOnlyList<GramBoxSandboxSession> GetSandboxesForChat(string chatId);
    GramBoxSandboxSession? GetSandbox(string sandboxId);

    Task<GramBoxSandboxSession> CreateSandboxAsync(
        string baseRepoPath, 
        string? branchName = null, 
        string? taskName = null, 
        CancellationToken ct = default, 
        string? runId = null, 
        SandboxNetworkMode networkMode = SandboxNetworkMode.Disabled, 
        IReadOnlyList<string>? allowedNetworkHosts = null,
        string? originatingChatId = null,
        string targetBranch = "main",
        SandboxResourceClass resourceClass = SandboxResourceClass.Small);

    Task<GramBoxExecutionResult> ExecuteAsync(string command, string? sandboxId = null, string? relativeSubDir = null, int timeoutSeconds = 120, CancellationToken ct = default);
    Task<bool> WriteFileAsync(string relativePath, string content, string? sandboxId = null, CancellationToken ct = default);
    Task<string?> ReadFileAsync(string relativePath, string? sandboxId = null, CancellationToken ct = default);
    Task<SandboxDiffSummary> GetDiffAsync(string? sandboxId = null, CancellationToken ct = default);
    Task<SandboxMergePreview> PreviewMergeAsync(string? sandboxId = null, string targetRef = "HEAD", CancellationToken ct = default);
    Task<SandboxCheckpoint?> CreateCheckpointAsync(string reason, string? sandboxId = null, CancellationToken ct = default);
    Task<bool> RollbackAsync(string checkpointId, string? sandboxId = null, CancellationToken ct = default);
    Task<bool> CommitChangesAsync(string commitMessage, string? sandboxId = null, CancellationToken ct = default);
    Task<bool> CloseSandboxAsync(string sandboxId, bool force = true, CancellationToken ct = default);
    void SelectActiveSession(string sandboxId);
}

public sealed class GramBoxService : IGramBoxService, IHostedService, IDisposable
{
    private readonly ILogger<GramBoxService> _logger;
    private readonly GramBoxWorktreeManager _worktreeManager;
    private readonly NativeScratchpadDriver _nativeDriver;
    private readonly ISandboxScheduler _scheduler;
    private readonly ISandboxMergeCoordinator _mergeCoordinator;
    private readonly ISandboxCheckpointService _checkpointService;
    private readonly SandboxAuditService? _audit;
    private readonly GramBoxConfig _config = new();

    private readonly ConcurrentDictionary<string, GramBoxSandboxSession> _sessions = new();
    private string? _activeSessionId;
    private string? _statusMessage = "Ready (Native Worktree Sandbox · 0MB Idle RAM)";
    private bool _isReady = true;

    public IReadOnlyList<GramBoxSandboxSession> ActiveSessions => _sessions.Values.OrderByDescending(s => s.LastActiveAt).ToList();
    public GramBoxSandboxSession? ActiveSession => !string.IsNullOrEmpty(_activeSessionId) && _sessions.TryGetValue(_activeSessionId, out var s) ? s : _sessions.Values.FirstOrDefault();
    public GramBoxEngineType ActiveEngine => GramBoxEngineType.NativeWorktree;
    public bool IsReady => _isReady;
    public string? StatusMessage => _statusMessage;

    public event Action? OnStatusChanged;
    public event Action<GramBoxSandboxSession, SandboxMajorState>? OnSandboxStateChanged;

    public GramBoxService(
        ILogger<GramBoxService> logger,
        GramBoxWorktreeManager worktreeManager,
        NativeScratchpadDriver nativeDriver,
        ISandboxScheduler? scheduler = null,
        ISandboxMergeCoordinator? mergeCoordinator = null,
        ISandboxCheckpointService? checkpointService = null,
        SandboxAuditService? audit = null)
    {
        _logger = logger;
        _worktreeManager = worktreeManager;
        _nativeDriver = nativeDriver;
        _scheduler = scheduler ?? new SandboxScheduler();
        _mergeCoordinator = mergeCoordinator ?? new SandboxMergeCoordinator(worktreeManager);
        _checkpointService = checkpointService ?? new SandboxCheckpointService(worktreeManager, new StorageService());
        _audit = audit;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _isReady = true;
        _statusMessage = "Ready (Native Worktree Engine · 0MB Idle RAM)";
        _logger.LogInformation("Gram Box: Initialized Native Worktree Sandbox Engine.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void NotifyState(string? message = null)
    {
        if (message != null) _statusMessage = message;
        try { OnStatusChanged?.Invoke(); } catch {}
    }

    private void NotifySandboxState(GramBoxSandboxSession session, SandboxMajorState state, string? detail = null)
    {
        session.MajorState = state;
        session.LastActiveAt = DateTime.UtcNow;
        if (detail != null) session.StateDetail = detail;
        try { OnSandboxStateChanged?.Invoke(session, state); } catch {}
        NotifyState($"[Sandbox: {session.Id}] {state}{(string.IsNullOrEmpty(detail) ? "" : $" · {detail}")}");
    }

    public void SubscribeChat(string sandboxId, string chatId)
    {
        if (string.IsNullOrWhiteSpace(sandboxId) || string.IsNullOrWhiteSpace(chatId)) return;
        if (_sessions.TryGetValue(sandboxId, out var session))
        {
            session.SubscribingChatIds.Add(chatId);
        }
    }

    public void UnsubscribeChat(string sandboxId, string chatId)
    {
        if (string.IsNullOrWhiteSpace(sandboxId) || string.IsNullOrWhiteSpace(chatId)) return;
        if (_sessions.TryGetValue(sandboxId, out var session))
        {
            session.SubscribingChatIds.Remove(chatId);
        }
    }

    public IReadOnlyList<GramBoxSandboxSession> GetSandboxesForChat(string chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId)) return [];
        return _sessions.Values
            .Where(s => s.OriginatingChatId.Equals(chatId, StringComparison.OrdinalIgnoreCase) ||
                        s.SubscribingChatIds.Contains(chatId))
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();
    }

    public GramBoxSandboxSession? GetSandbox(string sandboxId)
    {
        if (string.IsNullOrWhiteSpace(sandboxId)) return null;
        return _sessions.TryGetValue(sandboxId, out var s) ? s : null;
    }

    public void SelectActiveSession(string sandboxId)
    {
        if (_sessions.ContainsKey(sandboxId))
        {
            _activeSessionId = sandboxId;
            NotifyState();
        }
    }

    public async Task<GramBoxSandboxSession> CreateSandboxAsync(
        string baseRepoPath, 
        string? branchName = null, 
        string? taskName = null, 
        CancellationToken ct = default,
        string? runId = null,
        SandboxNetworkMode networkMode = SandboxNetworkMode.Disabled,
        IReadOnlyList<string>? allowedNetworkHosts = null,
        string? originatingChatId = null,
        string targetBranch = "main",
        SandboxResourceClass resourceClass = SandboxResourceClass.Small)
    {
        NotifyState("Creating isolated Git Worktree sandbox...");
        var lease = await _scheduler.AcquireAsync(new SandboxRequest(runId, resourceClass, networkMode, allowedNetworkHosts), ct);
        try
        {
            var session = await _worktreeManager.CreateWorktreeSessionAsync(baseRepoPath, branchName, taskName, ct);
            session.LeaseId = lease.Id;
            session.RunId = runId;
            session.ResourceBudget = lease.Budget;
            session.NetworkMode = lease.NetworkMode;
            session.AllowedNetworkHosts = lease.AllowedNetworkHosts.ToList();
            session.OriginatingChatId = originatingChatId ?? "";
            session.TargetBranch = targetBranch;
            if (!string.IsNullOrWhiteSpace(originatingChatId))
            {
                session.SubscribingChatIds.Add(originatingChatId);
            }
            session.MajorState = SandboxMajorState.Ready;
            session.TaskDescription = taskName;

            _sessions[session.Id] = session;
            _activeSessionId = session.Id;

            NotifySandboxState(session, SandboxMajorState.Ready, $"Worktree ready ({session.BranchName})");
            if (_audit != null) _ = _audit.RecordAsync(new SandboxAuditEntry(DateTimeOffset.UtcNow, "sandbox.created", session.Id, session.RunId, EnforcementMode: "native-worktree"));
            return session;
        }
        catch (Exception ex)
        {
            _scheduler.Release(lease.Id);
            _logger.LogError(ex, "Failed to create worktree sandbox");
            NotifyState($"Failed to create sandbox: {ex.Message}");
            throw;
        }
    }

    public async Task<GramBoxExecutionResult> ExecuteAsync(
        string command, 
        string? sandboxId = null, 
        string? relativeSubDir = null, 
        int timeoutSeconds = 120, 
        CancellationToken ct = default)
    {
        var targetSession = ResolveSession(sandboxId);
        if (targetSession == null)
        {
            targetSession = await CreateSandboxAsync(Environment.CurrentDirectory, null, "Default Sandbox", ct);
        }

        targetSession.CurrentCommand = command;
        NotifySandboxState(targetSession, SandboxMajorState.RunningCommand, command);
        if (!string.IsNullOrWhiteSpace(targetSession.LeaseId)) _scheduler.Heartbeat(targetSession.LeaseId);

        try
        {
            var result = await _nativeDriver.ExecuteCommandAsync(targetSession, command, relativeSubDir, timeoutSeconds, ct);
            targetSession.PeakWorkingSetBytes = Math.Max(targetSession.PeakWorkingSetBytes, result.PeakWorkingSetBytes);
            targetSession.CpuTimeMs += result.CpuTimeMs;

            // Automatically refresh modified files list
            try
            {
                var diff = await _worktreeManager.GetWorktreeDiffAsync(targetSession, ct);
                targetSession.Insertions = diff.Insertions;
                targetSession.Deletions = diff.Deletions;
                targetSession.ModifiedFiles = ExtractChangedFilesFromDiff(diff.DiffContent);
            }
            catch {}

            if (!string.IsNullOrWhiteSpace(targetSession.LeaseId)) _scheduler.Heartbeat(targetSession.LeaseId);
            if (_audit != null) _ = _audit.RecordAsync(new SandboxAuditEntry(DateTimeOffset.UtcNow, "command.completed", targetSession.Id, targetSession.RunId, command, result.Success, result.ElapsedMs, result.ErrorMessage, "native-worktree"));
            
            if (result.Success)
            {
                NotifySandboxState(targetSession, SandboxMajorState.Ready, $"Finished (Exit {result.ExitCode})");
            }
            else
            {
                targetSession.LastError = result.ErrorMessage ?? result.Stderr;
                NotifySandboxState(targetSession, SandboxMajorState.Error, $"Failed (Exit {result.ExitCode})");
            }

            return result;
        }
        catch (Exception ex)
        {
            targetSession.LastError = ex.Message;
            NotifySandboxState(targetSession, SandboxMajorState.Error, ex.Message);
            return new GramBoxExecutionResult
            {
                Success = false,
                ExitCode = -1,
                Stderr = ex.Message,
                ErrorMessage = ex.Message,
                SandboxId = targetSession.Id
            };
        }
    }

    public async Task<bool> WriteFileAsync(string relativePath, string content, string? sandboxId = null, CancellationToken ct = default)
    {
        var targetSession = ResolveSession(sandboxId);
        if (targetSession == null)
        {
            targetSession = await CreateSandboxAsync(Environment.CurrentDirectory, null, "Default Sandbox", ct);
        }

        var ok = await _nativeDriver.WriteFileAsync(targetSession, relativePath, content, ct);
        if (ok)
        {
            if (!targetSession.ModifiedFiles.Contains(relativePath))
            {
                targetSession.ModifiedFiles.Add(relativePath);
            }
            NotifySandboxState(targetSession, SandboxMajorState.Ready, $"Updated {relativePath}");
        }
        return ok;
    }

    public async Task<string?> ReadFileAsync(string relativePath, string? sandboxId = null, CancellationToken ct = default)
    {
        var targetSession = ResolveSession(sandboxId);
        if (targetSession == null) return null;

        return await _nativeDriver.ReadFileAsync(targetSession, relativePath, ct);
    }

    public async Task<SandboxDiffSummary> GetDiffAsync(string? sandboxId = null, CancellationToken ct = default)
    {
        var targetSession = ResolveSession(sandboxId);
        if (targetSession == null) return new SandboxDiffSummary();

        var diff = await _worktreeManager.GetWorktreeDiffAsync(targetSession, ct);
        targetSession.Insertions = diff.Insertions;
        targetSession.Deletions = diff.Deletions;
        targetSession.ModifiedFiles = ExtractChangedFilesFromDiff(diff.DiffContent);
        return diff;
    }

    public async Task<SandboxMergePreview> PreviewMergeAsync(string? sandboxId = null, string targetRef = "HEAD", CancellationToken ct = default)
    {
        var targetSession = ResolveSession(sandboxId);
        if (targetSession == null)
        {
            return new SandboxMergePreview { TargetRef = targetRef };
        }

        NotifySandboxState(targetSession, SandboxMajorState.Merging, $"Previewing merge against {targetRef}");
        var preview = await _mergeCoordinator.PreviewAsync(targetSession, targetRef, ct);
        
        if (!preview.CanMergeCleanly)
        {
            NotifySandboxState(targetSession, SandboxMajorState.Conflict, "Merge conflicts detected");
        }
        else
        {
            NotifySandboxState(targetSession, SandboxMajorState.Ready, "Clean merge verified");
        }

        return preview;
    }

    public async Task<SandboxCheckpoint?> CreateCheckpointAsync(string reason, string? sandboxId = null, CancellationToken ct = default)
    {
        var session = ResolveSession(sandboxId);
        if (session is null) return null;

        var cp = await _checkpointService.CreateAsync(session, reason, ct);
        if (cp is not null)
        {
            session.Checkpoints.Add(cp);
            NotifySandboxState(session, session.MajorState, $"Checkpoint created: {cp.Reason}");
        }
        return cp;
    }

    public async Task<bool> RollbackAsync(string checkpointId, string? sandboxId = null, CancellationToken ct = default)
    {
        var session = ResolveSession(sandboxId);
        if (session is null) return false;

        var ok = await _checkpointService.RollbackAsync(session, checkpointId, ct);
        if (ok)
        {
            NotifySandboxState(session, SandboxMajorState.Ready, $"Rolled back to {checkpointId}");
        }
        return ok;
    }

    public async Task<bool> CommitChangesAsync(string commitMessage, string? sandboxId = null, CancellationToken ct = default)
    {
        var targetSession = ResolveSession(sandboxId);
        if (targetSession == null) return false;

        NotifySandboxState(targetSession, SandboxMajorState.Merging, "Committing changes...");
        var ok = await _worktreeManager.CommitWorktreeChangesAsync(targetSession, commitMessage, ct);
        if (ok)
        {
            NotifySandboxState(targetSession, SandboxMajorState.Merged, $"Committed: {commitMessage}");
        }
        else
        {
            NotifySandboxState(targetSession, SandboxMajorState.Error, "Commit failed");
        }
        return ok;
    }

    public async Task<bool> CloseSandboxAsync(string sandboxId, bool force = true, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(sandboxId, out var session))
        {
            session.CompletedAt = DateTime.UtcNow;
            NotifySandboxState(session, SandboxMajorState.Disposed, "Worktree pruned");
            var ok = await _worktreeManager.RemoveWorktreeSessionAsync(session, force, ct);
            if (!string.IsNullOrWhiteSpace(session.LeaseId)) _scheduler.Release(session.LeaseId);
            if (_activeSessionId == sandboxId)
            {
                _activeSessionId = _sessions.Keys.FirstOrDefault();
            }
            if (_audit != null) _ = _audit.RecordAsync(new SandboxAuditEntry(DateTimeOffset.UtcNow, "sandbox.closed", sandboxId, session.RunId, Success: ok));
            return ok;
        }
        return false;
    }

    private static List<string> ExtractChangedFilesFromDiff(string diffContent)
    {
        if (string.IsNullOrWhiteSpace(diffContent)) return [];
        var files = new HashSet<string>();
        var lines = diffContent.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git a/", StringComparison.Ordinal))
            {
                var parts = line.Split(' ');
                if (parts.Length >= 4)
                {
                    var file = parts[2]["a/".Length..];
                    files.Add(file);
                }
            }
            else if (line.StartsWith("+++ b/", StringComparison.Ordinal))
            {
                var file = line["+++ b/".Length..].Trim();
                if (file != "/dev/null") files.Add(file);
            }
        }
        return files.ToList();
    }

    private GramBoxSandboxSession? ResolveSession(string? sandboxId)
    {
        if (!string.IsNullOrEmpty(sandboxId) && _sessions.TryGetValue(sandboxId, out var s)) return s;
        return ActiveSession;
    }

    public void Dispose()
    {
    }
}
