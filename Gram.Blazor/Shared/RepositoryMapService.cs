using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Gram.Blazor.Shared;

public sealed record SemanticSymbol(
    string Name,
    string Kind,
    string Signature,
    int LineNumber,
    string FilePath);

public sealed class CodeDependencyGraph
{
    public string RepositoryName { get; init; } = "";
    public string Revision { get; init; } = "";
    public IReadOnlyList<string> Nodes { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DefinedSymbols { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, HashSet<string>> Dependencies { get; init; } = new Dictionary<string, HashSet<string>>();
    public IReadOnlyDictionary<string, HashSet<string>> Dependents { get; init; } = new Dictionary<string, HashSet<string>>();
    public IReadOnlyDictionary<string, double> PageRankScores { get; init; } = new Dictionary<string, double>();
    public int TotalEdges { get; init; }
    public IReadOnlyList<SemanticSymbol> SemanticSymbols { get; init; } = [];
}

public sealed class RepositoryMapService
{
    private static readonly Regex SymbolPattern = new(
        @"\b(class|record|interface|struct|enum|trait|type)\s+([A-Za-z_][A-Za-z0-9_]*)|\b(?:public|private|protected|internal|static|async|virtual|override|sealed|partial|new|Task|void|string|int|bool|var)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(|\bdef\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(|\b(?:function|async\s+function|export\s+function)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(|\bfunc\s+(?:\([^)]*\)\s*)?([A-Za-z_][A-Za-z0-9_]*)\s*\(|\bfn\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ImportPattern = new(
        @"\busing\s+(?:static\s+)?([A-Za-z0-9_.]+);|\b(?:import|from)\s+['""]([^'""]+)['""]|\b(?:from\s+([A-Za-z0-9_.]+)\s+import|import\s+([A-Za-z0-9_.]+))|\buse\s+([A-Za-z0-9_:]+);",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordTokenPattern = new(
        @"\b[A-Za-z_][A-Za-z0-9_]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TypeDeclPattern = new(
        @"\b(?<kind>class|interface|record|struct|enum|trait|type)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MethodDeclPattern = new(
        @"\b(?:public|private|protected|internal|static|async|virtual|override|sealed|def|fn|func|function)?\s*(?:[A-Za-z0-9_<>\[\]?]+\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<params>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ConcurrentDictionary<string, CodeDependencyGraph> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string?> BuildRelevantMapAsync(
        string workspacePath,
        string? query,
        int maxCharacters = 6000,
        CancellationToken cancellationToken = default)
    {
        var graph = await BuildDependencyGraphAsync(workspacePath, cancellationToken);
        if (graph == null || graph.Nodes.Count == 0) return null;

        return Render(graph, query, maxCharacters);
    }

    public async Task<CodeDependencyGraph?> BuildDependencyGraphAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var root = await ResolveGitRootAsync(workspacePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            if (!string.IsNullOrWhiteSpace(workspacePath) && Directory.Exists(workspacePath))
            {
                root = Path.GetFullPath(workspacePath);
            }
            else
            {
                return null;
            }
        }

        var files = await ListTrackedFilesAsync(root, cancellationToken);
        if (files.Count == 0)
        {
            files = EnumerateFileSystemFiles(root);
        }
        if (files.Count == 0) return null;

        var revision = await GetRevisionAsync(root, cancellationToken) ?? "working-tree";
        var fingerprint = BuildFileFingerprint(root, files);
        var cacheKey = $"{root}|{revision}|{fingerprint}";
        if (!_cache.TryGetValue(cacheKey, out var cached))
        {
            cached = await IndexAndConstructGraphAsync(root, files, revision, cancellationToken);
            _cache[cacheKey] = cached;
            TrimCache();
        }

        return cached;
    }

    private static async Task<CodeDependencyGraph> IndexAndConstructGraphAsync(
        string root,
        IReadOnlyList<string> files,
        string revision,
        CancellationToken cancellationToken)
    {
        var repoName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var rawParsed = new List<RawFileIndex>();
        var allSemanticSymbols = new List<SemanticSymbol>();

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

            if (source.Length > 32000) source = source[..32000];

            var cleanPath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
            var fileSemantics = ExtractSemanticSymbols(source, cleanPath);
            allSemanticSymbols.AddRange(fileSemantics);

            // Extract defined symbols
            var symbols = SymbolPattern.Matches(source)
                .Select(ExtractSymbolName)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(symbol => symbol!)
                .Distinct(StringComparer.Ordinal)
                .Take(24)
                .ToList();

            // Extract imports
            var imports = ImportPattern.Matches(source)
                .Select(ExtractImportString)
                .Where(imp => !string.IsNullOrWhiteSpace(imp))
                .Select(imp => imp!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList();

            // Extract distinct token words for cross-file symbol usage
            var tokens = new HashSet<string>(
                WordTokenPattern.Matches(source)
                    .Select(m => m.Value)
                    .Where(w => w.Length >= 3),
                StringComparer.Ordinal);

            rawParsed.Add(new RawFileIndex(
                cleanPath,
                symbols,
                imports,
                tokens));
        }

        var nodes = rawParsed.Select(r => r.Path).ToList();
        var definedSymbols = rawParsed.ToDictionary(r => r.Path, r => (IReadOnlyList<string>)r.Symbols, StringComparer.OrdinalIgnoreCase);

        // Build symbol lookup table: SymbolName -> List of declaring file paths
        var symbolMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var raw in rawParsed)
        {
            foreach (var sym in raw.Symbols)
            {
                if (!symbolMap.TryGetValue(sym, out var list))
                {
                    list = [];
                    symbolMap[sym] = list;
                }
                list.Add(raw.Path);
            }
        }

        // Build graph adjacency lists
        var dependencies = nodes.ToDictionary(n => n, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var dependents = nodes.ToDictionary(n => n, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        var totalEdges = 0;
        foreach (var raw in rawParsed)
        {
            var u = raw.Path;

            // 1. Cross-file symbol references
            foreach (var token in raw.Tokens)
            {
                if (symbolMap.TryGetValue(token, out var declFiles))
                {
                    foreach (var v in declFiles)
                    {
                        if (!v.Equals(u, StringComparison.OrdinalIgnoreCase))
                        {
                            if (dependencies[u].Add(v))
                            {
                                dependents[v].Add(u);
                                totalEdges++;
                            }
                        }
                    }
                }
            }

            // 2. Import statements matching file paths or names
            foreach (var imp in raw.Imports)
            {
                var cleanImp = imp.Replace('\\', '/').Trim('/', '.');
                var impLeaf = Path.GetFileNameWithoutExtension(cleanImp);

                foreach (var other in nodes)
                {
                    if (other.Equals(u, StringComparison.OrdinalIgnoreCase)) continue;

                    var otherLeaf = Path.GetFileNameWithoutExtension(other);
                    if (other.Contains(cleanImp, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(impLeaf) && otherLeaf.Equals(impLeaf, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (dependencies[u].Add(other))
                        {
                            dependents[other].Add(u);
                            totalEdges++;
                        }
                    }
                }
            }
        }

        // Compute PageRank centrality
        var pageRankScores = ComputePageRank(nodes, dependencies, dependents);

        return new CodeDependencyGraph
        {
            RepositoryName = repoName,
            Revision = revision,
            Nodes = nodes,
            DefinedSymbols = definedSymbols,
            Dependencies = dependencies,
            Dependents = dependents,
            PageRankScores = pageRankScores,
            TotalEdges = totalEdges,
            SemanticSymbols = allSemanticSymbols
        };
    }

    private static Dictionary<string, double> ComputePageRank(
        IReadOnlyList<string> nodes,
        Dictionary<string, HashSet<string>> dependencies,
        Dictionary<string, HashSet<string>> dependents,
        double dampingFactor = 0.85,
        int iterations = 20)
    {
        var n = nodes.Count;
        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (n == 0) return scores;

        if (n == 1)
        {
            scores[nodes[0]] = 1.0;
            return scores;
        }

        var initialVal = 1.0 / n;
        foreach (var node in nodes) scores[node] = initialVal;

        for (var it = 0; it < iterations; it++)
        {
            var nextScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var danglingSum = 0.0;

            // Compute dangling sum (nodes with zero outgoing dependencies)
            foreach (var node in nodes)
            {
                if (dependencies[node].Count == 0)
                {
                    danglingSum += scores[node];
                }
            }

            var baseScore = (1.0 - dampingFactor) / n + (dampingFactor * danglingSum / n);

            foreach (var node in nodes)
            {
                var incomingSum = 0.0;
                // Sum contributions from nodes that depend on 'node' (incoming edges)
                foreach (var incoming in dependents[node])
                {
                    var outDegree = dependencies[incoming].Count;
                    if (outDegree > 0)
                    {
                        incomingSum += scores[incoming] / outDegree;
                    }
                }

                nextScores[node] = baseScore + (dampingFactor * incomingSum);
            }

            scores = nextScores;
        }

        return scores;
    }

    private static string Render(CodeDependencyGraph graph, string? query, int maxCharacters)
    {
        var queryTerms = (query ?? "")
            .Split(new[] { ' ', '\t', '\r', '\n', '.', '/', '\\', '-', '_', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // 1. Direct lexical scoring
        var directScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.Nodes)
        {
            directScores[node] = ScoreDirect(node, graph.DefinedSymbols.GetValueOrDefault(node) ?? [], queryTerms);
        }

        // 2. Graph neighborhood propagation
        var neighborBoosts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (node, dScore) in directScores)
        {
            if (dScore <= 0) continue;

            // Boost dependencies (files this relevant file uses)
            if (graph.Dependencies.TryGetValue(node, out var deps))
            {
                foreach (var dep in deps)
                {
                    neighborBoosts[dep] = neighborBoosts.GetValueOrDefault(dep) + (dScore * 0.35);
                }
            }

            // Boost dependents (callers of this relevant file)
            if (graph.Dependents.TryGetValue(node, out var callers))
            {
                foreach (var caller in callers)
                {
                    neighborBoosts[caller] = neighborBoosts.GetValueOrDefault(caller) + (dScore * 0.25);
                }
            }
        }

        // 3. Combine Direct + Neighbor Boost + PageRank Centrality
        var ranked = graph.Nodes
            .Select(node =>
            {
                var dScore = directScores.GetValueOrDefault(node);
                var nBoost = neighborBoosts.GetValueOrDefault(node);
                var pr = graph.PageRankScores.GetValueOrDefault(node);
                var finalScore = dScore + nBoost + (pr * 50.0);
                return (Node: node, Score: finalScore, PageRank: pr);
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.PageRank)
            .ThenBy(item => item.Node, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Node)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<repository_map>");
        sb.AppendLine($"Repository: {graph.RepositoryName}");
        sb.AppendLine($"Revision: {graph.Revision}");
        sb.AppendLine($"Architecture Graph: {graph.Nodes.Count} files, {graph.TotalEdges} dependency links");
        sb.AppendLine("Relevant indexed files and symbols:");

        var topNodesSet = new HashSet<string>(ranked.Take(30), StringComparer.OrdinalIgnoreCase);

        foreach (var node in ranked)
        {
            var symbols = graph.DefinedSymbols.GetValueOrDefault(node) ?? [];
            var sbLine = new StringBuilder();
            sbLine.Append($"- {node}");

            // Include top 2 connected links within top nodes if present
            if (graph.Dependencies.TryGetValue(node, out var deps) && deps.Count > 0)
            {
                var linked = deps.Where(d => topNodesSet.Contains(d)).Take(2).Select(Path.GetFileName).ToList();
                if (linked.Count > 0)
                {
                    sbLine.Append($" [uses: {string.Join(", ", linked)}]");
                }
            }

            if (symbols.Count > 0)
            {
                sbLine.Append($" :: {string.Join(", ", symbols.Take(12))}");
            }

            var line = sbLine.ToString();
            if (sb.Length + line.Length + 32 > maxCharacters) break;
            sb.AppendLine(line);
        }

        if (graph.SemanticSymbols.Count > 0 && sb.Length < maxCharacters - 300)
        {
            sb.AppendLine("\n<semantic_symbols>");
            foreach (var sym in graph.SemanticSymbols.Where(s => s.Kind is "class" or "interface" or "record").Take(15))
            {
                var symLine = $"- [{sym.Kind}] {sym.Name} in {sym.FilePath}:{sym.LineNumber} -> `{sym.Signature}`";
                if (sb.Length + symLine.Length + 24 > maxCharacters) break;
                sb.AppendLine(symLine);
            }
            sb.AppendLine("</semantic_symbols>");
        }

        sb.AppendLine("</repository_map>");
        return sb.ToString();
    }

    private static int ScoreDirect(string path, IReadOnlyList<string> symbols, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0) return 0;

        var score = 0;
        foreach (var term in terms)
        {
            if (path.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 12;
            score += symbols.Count(symbol => symbol.Contains(term, StringComparison.OrdinalIgnoreCase)) * 8;
        }

        return score;
    }

    private static string? ExtractSymbolName(Match match)
    {
        for (var i = 2; i <= 6; i++)
        {
            if (match.Groups[i].Success && !string.IsNullOrWhiteSpace(match.Groups[i].Value))
            {
                return match.Groups[i].Value;
            }
        }
        return null;
    }

    private static string? ExtractImportString(Match match)
    {
        for (var i = 1; i <= 5; i++)
        {
            if (match.Groups[i].Success && !string.IsNullOrWhiteSpace(match.Groups[i].Value))
            {
                return match.Groups[i].Value;
            }
        }
        return null;
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

    private static List<string> EnumerateFileSystemFiles(string root)
    {
        if (!Directory.Exists(root)) return [];
        var result = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(root);

        var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist", "build", "target", ".gram", ".vscode"
        };

        while (queue.Count > 0 && result.Count < 1600)
        {
            var current = queue.Dequeue();
            try
            {
                foreach (var dir in Directory.GetDirectories(current))
                {
                    var name = Path.GetFileName(dir);
                    if (!excludedDirs.Contains(name) && !name.StartsWith('.'))
                    {
                        queue.Enqueue(dir);
                    }
                }

                foreach (var file in Directory.GetFiles(current))
                {
                    var rel = Path.GetRelativePath(root, file);
                    if (IsIndexable(rel))
                    {
                        result.Add(rel.Replace('\\', '/'));
                    }
                }
            }
            catch
            {
                // Ignore IO or access permission errors during traversal
            }
        }

        return result;
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

    public IReadOnlyList<SemanticSymbol> FindSymbols(string workspacePath, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var graph = GetActiveGraph(workspacePath);
        if (graph == null || graph.SemanticSymbols.Count == 0) return [];

        return graph.SemanticSymbols
            .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        s.Signature.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();
    }

    public SemanticSymbol? FindDefinition(string workspacePath, string symbolName)
    {
        if (string.IsNullOrWhiteSpace(symbolName)) return null;
        var graph = GetActiveGraph(workspacePath);
        if (graph == null || graph.SemanticSymbols.Count == 0) return null;

        return graph.SemanticSymbols
            .FirstOrDefault(s => s.Name.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
            ?? graph.SemanticSymbols
                .FirstOrDefault(s => s.Name.Contains(symbolName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SemanticSymbol?> FindDefinitionAsync(string workspacePath, string symbolName, CancellationToken cancellationToken = default)
    {
        var def = FindDefinition(workspacePath, symbolName);
        if (def != null) return def;

        var graph = await BuildDependencyGraphAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        if (graph == null || graph.SemanticSymbols.Count == 0) return null;

        return graph.SemanticSymbols
            .FirstOrDefault(s => s.Name.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
            ?? graph.SemanticSymbols
                .FirstOrDefault(s => s.Name.Contains(symbolName, StringComparison.OrdinalIgnoreCase));
    }

    public static List<SemanticSymbol> ExtractSemanticSymbols(string source, string filePath)
    {
        var results = new List<SemanticSymbol>();
        var lines = source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length < 3 || line.StartsWith("//") || line.StartsWith("#") || line.StartsWith("/*") || line.StartsWith("*"))
                continue;

            // 1. Type Declarations
            var typeMatch = TypeDeclPattern.Match(line);
            if (typeMatch.Success)
            {
                var kind = typeMatch.Groups["kind"].Value;
                var name = typeMatch.Groups["name"].Value;
                var sig = line.TrimEnd('{').Trim();
                results.Add(new SemanticSymbol(name, kind, sig, i + 1, filePath));
                continue;
            }

            // 2. Method / Function Declarations
            var methodMatch = MethodDeclPattern.Match(line);
            if (methodMatch.Success)
            {
                var name = methodMatch.Groups["name"].Value;
                if (!IsLanguageKeyword(name))
                {
                    var sig = line.TrimEnd('{').Trim();
                    results.Add(new SemanticSymbol(name, "method", sig, i + 1, filePath));
                }
            }
        }

        return results;
    }

    private static bool IsLanguageKeyword(string name)
    {
        return name is "if" or "for" or "while" or "switch" or "catch" or "using" or "lock" or "return" or "sizeof" or "typeof" or "nameof";
    }

    private CodeDependencyGraph? GetActiveGraph(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath)) return _cache.Values.LastOrDefault();
        var folderName = Path.GetFileName(workspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return _cache.Values.FirstOrDefault(g => g.RepositoryName.Equals(folderName, StringComparison.OrdinalIgnoreCase))
            ?? _cache.Values.LastOrDefault();
    }

    private void TrimCache()
    {
        if (_cache.Count <= 8) return;
        foreach (var key in _cache.Keys.Take(_cache.Count - 8)) _cache.TryRemove(key, out _);
    }

    private sealed record RawFileIndex(
        string Path,
        List<string> Symbols,
        List<string> Imports,
        HashSet<string> Tokens);
}
