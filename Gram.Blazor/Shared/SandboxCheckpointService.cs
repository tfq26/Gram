using System.Text.Json;

namespace Gram.Blazor.Shared;

public interface ISandboxCheckpointService
{
    Task<SandboxCheckpoint?> CreateAsync(GramBoxSandboxSession session, string reason, CancellationToken cancellationToken = default);
    Task<bool> RollbackAsync(GramBoxSandboxSession session, string checkpointId, CancellationToken cancellationToken = default);
    IReadOnlyList<SandboxCheckpoint> Get(string sandboxId);
}

public sealed class SandboxCheckpointService : ISandboxCheckpointService
{
    private readonly GramBoxWorktreeManager _worktreeManager;
    private readonly StorageService _storage;
    private readonly Dictionary<string, List<SandboxCheckpoint>> _checkpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public SandboxCheckpointService(GramBoxWorktreeManager worktreeManager, StorageService storage)
    {
        _worktreeManager = worktreeManager;
        _storage = storage;
    }

    public async Task<SandboxCheckpoint?> CreateAsync(GramBoxSandboxSession session, string reason, CancellationToken cancellationToken = default)
    {
        var diff = await _worktreeManager.GetWorktreeDiffAsync(session, cancellationToken);
        var directory = _storage.GetFilePath(Path.Combine("sandbox-checkpoints", session.Id));
        Directory.CreateDirectory(directory);
        var diffPath = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.diff");
        await File.WriteAllTextAsync(diffPath, diff.DiffContent ?? "", cancellationToken);
        var checkpoint = new SandboxCheckpoint { SandboxId = session.Id, RunId = session.RunId, Reason = reason.Trim(), DiffPath = diffPath, FilesChanged = diff.FilesChanged };
        await File.WriteAllTextAsync(Path.Combine(directory, $"{checkpoint.Id}.json"), JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        lock (_sync)
        {
            if (!_checkpoints.TryGetValue(session.Id, out var list)) _checkpoints[session.Id] = list = [];
            list.Add(checkpoint);
        }
        return checkpoint;
    }

    public async Task<bool> RollbackAsync(GramBoxSandboxSession session, string checkpointId, CancellationToken cancellationToken = default)
    {
        SandboxCheckpoint? checkpoint;
        lock (_sync)
        {
            if (!_checkpoints.TryGetValue(session.Id, out var list)) return false;
            checkpoint = list.FirstOrDefault(item => item.Id.Equals(checkpointId, StringComparison.OrdinalIgnoreCase));
            if (checkpoint is null) return false;
        }
        if (!await _worktreeManager.ResetWorktreeAsync(session, cancellationToken)) return false;
        return string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(checkpoint.DiffPath, cancellationToken))
            || await _worktreeManager.ApplyPatchAsync(session, checkpoint.DiffPath, cancellationToken);
    }

    public IReadOnlyList<SandboxCheckpoint> Get(string sandboxId)
    {
        lock (_sync) return _checkpoints.TryGetValue(sandboxId, out var list) ? list.ToList() : [];
    }
}
