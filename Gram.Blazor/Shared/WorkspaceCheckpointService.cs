using System.Collections.Concurrent;
using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed record WorkspaceFileCheckpoint(
    string Id,
    string WorkspacePath,
    string RelativePath,
    string? OriginalContent,
    bool ExistedPrior,
    DateTimeOffset Timestamp,
    string Reason,
    string? RunId = null);

public interface IWorkspaceCheckpointService
{
    Task<WorkspaceFileCheckpoint> CapturePreWriteSnapshotAsync(
        string workspacePath,
        string relativePath,
        string reason,
        string? runId = null,
        CancellationToken ct = default);

    Task<bool> RollbackCheckpointAsync(string checkpointId, CancellationToken ct = default);
    Task<bool> RollbackLatestAsync(string workspacePath, CancellationToken ct = default);
    IReadOnlyList<WorkspaceFileCheckpoint> GetHistory(string workspacePath);
    void ClearHistory(string? workspacePath = null);
}

public sealed class WorkspaceCheckpointService : IWorkspaceCheckpointService
{
    private readonly ConcurrentDictionary<string, WorkspaceFileCheckpoint> _checkpointsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<string>> _checkpointsByWorkspace = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public async Task<WorkspaceFileCheckpoint> CapturePreWriteSnapshotAsync(
        string workspacePath,
        string relativePath,
        string reason,
        string? runId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var canonicalRoot = Path.GetFullPath(workspacePath);
        var cleanRel = relativePath.Trim().Replace('\\', '/').TrimStart('/');
        var targetFullPath = Path.GetFullPath(Path.Combine(canonicalRoot, cleanRel));

        bool existed = File.Exists(targetFullPath);
        string? originalContent = null;

        if (existed)
        {
            originalContent = await File.ReadAllTextAsync(targetFullPath, ct);
        }

        var id = $"chk-{Guid.NewGuid():N}"[..12];
        var checkpoint = new WorkspaceFileCheckpoint(
            Id: id,
            WorkspacePath: canonicalRoot,
            RelativePath: cleanRel,
            OriginalContent: originalContent,
            ExistedPrior: existed,
            Timestamp: DateTimeOffset.UtcNow,
            Reason: reason,
            RunId: runId);

        _checkpointsById[id] = checkpoint;
        _checkpointsByWorkspace.AddOrUpdate(
            canonicalRoot,
            _ => [id],
            (_, list) =>
            {
                lock (_lock)
                {
                    list.Add(id);
                }
                return list;
            });

        // Persist checkpoint to disk for durability across crashes (.gram/checkpoints/)
        try
        {
            var checkpointsDir = Path.Combine(canonicalRoot, ".gram", "checkpoints");
            if (!Directory.Exists(checkpointsDir))
            {
                Directory.CreateDirectory(checkpointsDir);
            }

            var jsonFile = Path.Combine(checkpointsDir, $"{id}.json");
            var json = JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonFile, json, ct);
        }
        catch
        {
            // In-memory snapshot remains valid even if writing metadata fails
        }

        return checkpoint;
    }

    public async Task<bool> RollbackCheckpointAsync(string checkpointId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(checkpointId)) return false;

        if (!_checkpointsById.TryGetValue(checkpointId.Trim(), out var checkpoint))
        {
            return false;
        }

        var targetFullPath = Path.GetFullPath(Path.Combine(checkpoint.WorkspacePath, checkpoint.RelativePath));

        if (checkpoint.ExistedPrior)
        {
            var dir = Path.GetDirectoryName(targetFullPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllTextAsync(targetFullPath, checkpoint.OriginalContent ?? string.Empty, ct);
        }
        else
        {
            // File was created newly by the operation; delete it to roll back
            if (File.Exists(targetFullPath))
            {
                File.Delete(targetFullPath);
            }
        }

        return true;
    }

    public async Task<bool> RollbackLatestAsync(string workspacePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workspacePath)) return false;

        var canonicalRoot = Path.GetFullPath(workspacePath);
        if (!_checkpointsByWorkspace.TryGetValue(canonicalRoot, out var list))
        {
            return false;
        }

        string? latestId = null;
        lock (_lock)
        {
            if (list.Count > 0)
            {
                latestId = list[^1];
                list.RemoveAt(list.Count - 1);
            }
        }

        if (latestId == null) return false;
        return await RollbackCheckpointAsync(latestId, ct);
    }

    public IReadOnlyList<WorkspaceFileCheckpoint> GetHistory(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath)) return [];

        var canonicalRoot = Path.GetFullPath(workspacePath);
        if (!_checkpointsByWorkspace.TryGetValue(canonicalRoot, out var list))
        {
            return [];
        }

        lock (_lock)
        {
            return list
                .Select(id => _checkpointsById.TryGetValue(id, out var cp) ? cp : null)
                .Where(cp => cp != null)
                .Cast<WorkspaceFileCheckpoint>()
                .OrderByDescending(cp => cp.Timestamp)
                .ToList();
        }
    }

    public void ClearHistory(string? workspacePath = null)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            _checkpointsById.Clear();
            _checkpointsByWorkspace.Clear();
            return;
        }

        var canonicalRoot = Path.GetFullPath(workspacePath);
        if (_checkpointsByWorkspace.TryRemove(canonicalRoot, out var list))
        {
            foreach (var id in list)
            {
                _checkpointsById.TryRemove(id, out _);
            }
        }
    }
}
