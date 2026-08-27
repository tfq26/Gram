namespace Gram.Blazor.Shared;

public static class SandboxPathGuard
{
    public static bool TryResolve(string sandboxRoot, string? relativePath, out string resolvedPath, out string? error)
    {
        resolvedPath = "";
        error = null;
        if (string.IsNullOrWhiteSpace(sandboxRoot)) { error = "Sandbox root is empty."; return false; }
        try
        {
            var root = Path.GetFullPath(sandboxRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath ?? ""));
            var relative = Path.GetRelativePath(root, candidate);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                error = $"Path escapes sandbox root: {relativePath}";
                return false;
            }
            if (ContainsSymlinkOutsideRoot(root, candidate))
            {
                error = $"Path traverses a symbolic link: {relativePath}";
                return false;
            }
            resolvedPath = candidate;
            return true;
        }
        catch (Exception ex) { error = $"Invalid sandbox path: {ex.Message}"; return false; }
    }

    private static bool ContainsSymlinkOutsideRoot(string root, string candidate)
    {
        var current = root;
        var relative = Path.GetRelativePath(root, candidate);
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!File.Exists(current) && !Directory.Exists(current)) continue;
            var info = new FileInfo(current);
            if (info.LinkTarget is not null) return true;
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget is not null) return true;
        }
        return false;
    }
}
