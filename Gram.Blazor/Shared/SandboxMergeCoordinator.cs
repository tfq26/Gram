namespace Gram.Blazor.Shared;

public interface ISandboxMergeCoordinator
{
    Task<SandboxMergePreview> PreviewAsync(GramBoxSandboxSession session, string targetRef = "HEAD", CancellationToken cancellationToken = default);
}

public sealed class SandboxMergeCoordinator : ISandboxMergeCoordinator
{
    private readonly GramBoxWorktreeManager _worktreeManager;
    private readonly SemaphoreSlim _mergeQueue = new(1, 1);

    public SandboxMergeCoordinator(GramBoxWorktreeManager worktreeManager) => _worktreeManager = worktreeManager;

    public async Task<SandboxMergePreview> PreviewAsync(GramBoxSandboxSession session, string targetRef = "HEAD", CancellationToken cancellationToken = default)
    {
        await _mergeQueue.WaitAsync(cancellationToken);
        try { return await _worktreeManager.PreviewMergeAsync(session, targetRef, cancellationToken); }
        finally { _mergeQueue.Release(); }
    }
}
