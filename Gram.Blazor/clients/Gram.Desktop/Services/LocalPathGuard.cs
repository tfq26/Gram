namespace Gram.Desktop;

public static class LocalPathGuard
{
    /// <summary>
    /// Verifies that the requested path is fully contained within the root workspace directory,
    /// preventing directory traversal attacks using '..' or absolute paths escaping root.
    /// </summary>
    public static bool IsWithinRoot(string rootPath, string relativeOrSubPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(relativeOrSubPath)) return false;
        try
        {
            var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var combined = Path.GetFullPath(Path.Combine(fullRoot, relativeOrSubPath));

            return combined.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   combined.Equals(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
