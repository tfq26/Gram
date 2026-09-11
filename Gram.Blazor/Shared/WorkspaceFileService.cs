namespace Gram.Blazor.Shared;

public sealed record WorkspaceFileEntry(
    string RelativePath,
    string FileName,
    string Extension,
    long SizeBytes,
    DateTime LastModifiedUtc
);

public sealed class WorkspaceFileService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedRoot;
    private DateTime _lastScanTime = DateTime.MinValue;
    private List<WorkspaceFileEntry> _cachedEntries = new();

    public IReadOnlyList<WorkspaceFileEntry> CachedEntries => _cachedEntries;

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".vscode", "bin", "obj", "node_modules", "dist", "build", ".gemini", ".idea"
    };

    public async Task<IReadOnlyList<WorkspaceFileEntry>> GetWorkspaceFilesAsync(string workspaceRoot, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return Array.Empty<WorkspaceFileEntry>();

        var now = DateTime.UtcNow;
        if (!forceRefresh && _cachedRoot == workspaceRoot && (now - _lastScanTime).TotalSeconds < 25)
        {
            return _cachedEntries;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _cachedRoot == workspaceRoot && (DateTime.UtcNow - _lastScanTime).TotalSeconds < 25)
            {
                return _cachedEntries;
            }

            var entries = await Task.Run(() => ScanDirectory(workspaceRoot), ct);
            _cachedRoot = workspaceRoot;
            _cachedEntries = entries;
            _lastScanTime = DateTime.UtcNow;
            return _cachedEntries;
        }
        catch (OperationCanceledException)
        {
            return _cachedEntries;
        }
        catch (Exception ex)
        {
            GramLog.Warn("WorkspaceFileService", $"Failed to scan workspace files: {ex.Message}");
            return _cachedEntries;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static List<WorkspaceFileEntry> ScanDirectory(string root)
    {
        var result = new List<WorkspaceFileEntry>();
        var rootDir = new DirectoryInfo(root);
        if (!rootDir.Exists) return result;

        var stack = new Stack<DirectoryInfo>();
        stack.Push(rootDir);

        while (stack.Count > 0 && result.Count < 2500)
        {
            var dir = stack.Pop();

            try
            {
                foreach (var subDir in dir.EnumerateDirectories())
                {
                    if (!IgnoredDirectories.Contains(subDir.Name) && !subDir.Attributes.HasFlag(FileAttributes.Hidden))
                    {
                        stack.Push(subDir);
                    }
                }

                foreach (var file in dir.EnumerateFiles())
                {
                    if (file.Attributes.HasFlag(FileAttributes.Hidden)) continue;

                    var rel = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
                    result.Add(new WorkspaceFileEntry(
                        rel,
                        file.Name,
                        file.Extension.ToLowerInvariant(),
                        file.Length,
                        file.LastWriteTimeUtc
                    ));

                    if (result.Count >= 2500) break;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return result;
    }
}
