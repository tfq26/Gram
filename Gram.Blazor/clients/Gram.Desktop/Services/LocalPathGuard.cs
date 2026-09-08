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
            var trimmed = relativeOrSubPath.Trim();
            // Explicitly reject Windows drive-letter roots on all platforms (e.g. C:\...)
            if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            {
                return false;
            }

            var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedSub = trimmed.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var combined = Path.GetFullPath(Path.Combine(fullRoot, normalizedSub));

            return combined.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   combined.Equals(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
