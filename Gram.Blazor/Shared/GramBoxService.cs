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

    Task<GramBoxSandboxSession> CreateSandboxAsync(string baseRepoPath, string? branchName = null, string? taskName = null, CancellationToken ct = default, string? runId = null, SandboxNetworkMode networkMode = SandboxNetworkMode.Disabled, IReadOnlyList<string>? allowedNetworkHosts = null);
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

    public GramBoxService(
        ILogger<GramBoxService> logger,
        GramBoxWorktreeManager worktreeManager,
        NativeScratchpadDriver nativeDriver,
        ISandboxScheduler? scheduler = null,
        ISandboxMergeCoordinator? mergeCoordinator = null,
        ISandboxCheckpointService? checkpointService = null)
    {
        _logger = logger;
        _worktreeManager = worktreeManager;
        _nativeDriver = nativeDriver;
        _scheduler = scheduler ?? new SandboxScheduler();
        _mergeCoordinator = mergeCoordinator ?? new SandboxMergeCoordinator(worktreeManager);
        _checkpointService = checkpointService ?? new SandboxCheckpointService(worktreeManager, new StorageService());
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 100% Instant, non-blocking startup: 0ms delay to UI
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
        IReadOnlyList<string>? allowedNetworkHosts = null)
    {
        NotifyState("Creating isolated Git Worktree sandbox...");
        var lease = await _scheduler.AcquireAsync(new SandboxRequest(runId, NetworkMode: networkMode, AllowedNetworkHosts: allowedNetworkHosts), ct);
        try
        {
            var session = await _worktreeManager.CreateWorktreeSessionAsync(baseRepoPath, branchName, taskName, ct);
            session.LeaseId = lease.Id;
            session.RunId = runId;
            session.ResourceBudget = lease.Budget;
            session.NetworkMode = lease.NetworkMode;
            session.AllowedNetworkHosts = lease.AllowedNetworkHosts.ToList();
            _sessions[session.Id] = session;
            _activeSessionId = session.Id;

            NotifyState($"Sandbox '{session.Name}' ready ({session.BranchName})");
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
            // Auto-create default sandbox if none exists
            targetSession = await CreateSandboxAsync(Environment.CurrentDirectory, null, "Default Sandbox", ct);
        }

        NotifyState($"[Sandbox: {targetSession.Id}] Running: {command}");
        if (!string.IsNullOrWhiteSpace(targetSession.LeaseId)) _scheduler.Heartbeat(targetSession.LeaseId);
        try
        {
            var result = await _nativeDriver.ExecuteCommandAsync(targetSession, command, relativeSubDir, timeoutSeconds, ct);
            if (!string.IsNullOrWhiteSpace(targetSession.LeaseId)) _scheduler.Heartbeat(targetSession.LeaseId);
            NotifyState($"[Sandbox: {targetSession.Id}] Command finished (Exit {result.ExitCode})");
            return result;
        }
        catch (Exception ex)
        {
            NotifyState($"[Sandbox: {targetSession.Id}] Error: {ex.Message}");
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

        return await _nativeDriver.WriteFileAsync(targetSession, relativePath, content, ct);
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

        return await _worktreeManager.GetWorktreeDiffAsync(targetSession, ct);
    }

    public async Task<SandboxMergePreview> PreviewMergeAsync(string? sandboxId = null, string targetRef = "HEAD", CancellationToken ct = default)
    {
        var targetSession = ResolveSession(sandboxId);
        return targetSession == null
            ? new SandboxMergePreview { TargetRef = targetRef }
            : await _mergeCoordinator.PreviewAsync(targetSession, targetRef, ct);
    }

    public async Task<SandboxCheckpoint?> CreateCheckpointAsync(string reason, string? sandboxId = null, CancellationToken ct = default)
    {
        var session = ResolveSession(sandboxId);
        return session is null ? null : await _checkpointService.CreateAsync(session, reason, ct);
    }

    public async Task<bool> RollbackAsync(string checkpointId, string? sandboxId = null, CancellationToken ct = default)
    {
        var session = ResolveSession(sandboxId);
        return session is not null && await _checkpointService.RollbackAsync(session, checkpointId, ct);
    }

    public async Task<bool> CommitChangesAsync(string commitMessage, string? sandboxId = null, CancellationToken ct = default)
    {
        var targetSession = ResolveSession(sandboxId);
        if (targetSession == null) return false;

        var ok = await _worktreeManager.CommitWorktreeChangesAsync(targetSession, commitMessage, ct);
        if (ok) NotifyState($"Committed changes in sandbox {targetSession.Id}");
        return ok;
    }

    public async Task<bool> CloseSandboxAsync(string sandboxId, bool force = true, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(sandboxId, out var session))
        {
            var ok = await _worktreeManager.RemoveWorktreeSessionAsync(session, force, ct);
            if (!string.IsNullOrWhiteSpace(session.LeaseId)) _scheduler.Release(session.LeaseId);
            if (_activeSessionId == sandboxId)
            {
                _activeSessionId = _sessions.Keys.FirstOrDefault();
            }
            NotifyState($"Closed sandbox {sandboxId}");
            return ok;
        }
        return false;
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
