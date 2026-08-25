using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Gram.Blazor.Shared;

public sealed class RepositoryMapService
{
    private static readonly Regex SymbolPattern = new(
        @"\b(class|record|interface|struct|enum)\s+([A-Za-z_][A-Za-z0-9_]*)|\b(?:public|private|protected|internal|static|async|virtual|override|sealed|partial|new|Task|void|string|int|bool|var)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ConcurrentDictionary<string, CachedRepositoryMap> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string?> BuildRelevantMapAsync(
        string workspacePath,
        string? query,
        int maxCharacters = 6000,
        CancellationToken cancellationToken = default)
    {
        var root = await ResolveGitRootAsync(workspacePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;

        var files = await ListTrackedFilesAsync(root, cancellationToken);
        if (files.Count == 0) return null;

        var revision = await GetRevisionAsync(root, cancellationToken) ?? "working-tree";
        var fingerprint = BuildFileFingerprint(root, files);
        var cacheKey = $"{root}|{revision}|{fingerprint}";
        if (!_cache.TryGetValue(cacheKey, out var cached))
        {
            cached = await IndexFilesAsync(root, files, revision, cancellationToken);
            _cache[cacheKey] = cached;
            TrimCache();
        }

        return Render(cached, query, maxCharacters);
    }

    private static async Task<CachedRepositoryMap> IndexFilesAsync(
        string root,
        IReadOnlyList<string> files,
        string revision,
        CancellationToken cancellationToken)
    {
        var entries = new List<RepositoryMapEntry>();
        foreach (var relativePath in files.Take(1600))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsIndexable(relativePath)) continue;

            var fullPath = Path.Combine(root, relativePath);
            if (!File.Exists(fullPath)) continue;

            string source;
            try
            {
                source = await File.ReadAllTextAsync(fullPath, cancellationToken);
            }
            catch
            {
                continue;
            }

            if (source.Length > 24000) source = source[..24000];
            var symbols = SymbolPattern.Matches(source)
                .Select(match => match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Distinct(StringComparer.Ordinal)
                .Take(18)
                .ToList();

            entries.Add(new RepositoryMapEntry(relativePath.Replace(Path.DirectorySeparatorChar, '/'), symbols));
        }

        return new CachedRepositoryMap(
            Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            revision,
            entries);
    }

    private static string Render(CachedRepositoryMap map, string? query, int maxCharacters)
    {
        var queryTerms = (query ?? "")
            .Split(new[] { ' ', '\t', '\r', '\n', '.', '/', '\\', '-', '_', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var ranked = map.Entries
            .Select(entry => (entry, score: Score(entry, queryTerms)))
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.entry)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<repository_map>");
        sb.AppendLine($"Repository: {map.RepositoryName}");
        sb.AppendLine($"Revision: {map.Revision}");
        sb.AppendLine("Relevant indexed files and symbols:");

        foreach (var entry in ranked)
        {
            var line = entry.Symbols.Count == 0
                ? $"- {entry.Path}"
                : $"- {entry.Path} :: {string.Join(", ", entry.Symbols)}";

            if (sb.Length + line.Length + 32 > maxCharacters) break;
            sb.AppendLine(line);
        }

        sb.AppendLine("</repository_map>");
        return sb.ToString();
    }

    private static int Score(RepositoryMapEntry entry, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0) return Math.Min(10, entry.Symbols.Count);

        var score = 0;
        foreach (var term in terms)
        {
            if (entry.Path.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 8;
            score += entry.Symbols.Count(symbol => symbol.Contains(term, StringComparison.OrdinalIgnoreCase)) * 5;
        }

        return score;
    }

    private static bool IsIndexable(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)) return false;

        return Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".razor" or ".js" or ".ts" or ".tsx" or ".jsx" or ".py" or ".rs" or ".go" or ".java" or ".kt" or ".sql" or ".cpp" or ".h" or ".csproj";
    }

    private static string BuildFileFingerprint(string root, IReadOnlyList<string> files)
    {
        var builder = new StringBuilder();
        foreach (var relativePath in files.Take(1600))
        {
            var fullPath = Path.Combine(root, relativePath);
            if (!File.Exists(fullPath)) continue;
            var info = new FileInfo(fullPath);
            builder.Append(relativePath).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append(';');
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16];
    }

    private static async Task<string?> ResolveGitRootAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(workspacePath, cancellationToken, "rev-parse", "--show-toplevel");
        return output.ExitCode == 0 ? output.Stdout.Trim() : null;
    }

    private static async Task<List<string>> ListTrackedFilesAsync(string root, CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(root, cancellationToken, "ls-files", "-z");
        if (output.ExitCode != 0) return [];
        return output.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static async Task<string?> GetRevisionAsync(string root, CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(root, cancellationToken, "rev-parse", "--short", "HEAD");
        return output.ExitCode == 0 ? output.Stdout.Trim() : null;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunGitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (-1, "", ex.Message); }
    }

    private void TrimCache()
    {
        if (_cache.Count <= 8) return;
        foreach (var key in _cache.Keys.Take(_cache.Count - 8)) _cache.TryRemove(key, out _);
    }

    private sealed record RepositoryMapEntry(string Path, IReadOnlyList<string> Symbols);
    private sealed record CachedRepositoryMap(string RepositoryName, string Revision, IReadOnlyList<RepositoryMapEntry> Entries);
}
