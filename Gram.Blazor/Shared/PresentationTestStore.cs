namespace Gram.Blazor.Shared;

public sealed class PresentationTestStore
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gram-presentation-tests");
    private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public PresentationTestStore()
    {
        Directory.CreateDirectory(_root);
        foreach (var directory in Directory.GetDirectories(_root))
        {
            try
            {
                if (Directory.GetCreationTimeUtc(directory) < DateTime.UtcNow.AddHours(-24)) Directory.Delete(directory, true);
            }
            catch { }
        }
    }

    public string CreateDirectory(string id)
    {
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void Register(string id, string path) { lock (_sync) _paths[id] = path; }

    public string? GetPath(string id)
    {
        lock (_sync) return _paths.TryGetValue(id, out var path) && File.Exists(path) ? path : null;
    }
}
