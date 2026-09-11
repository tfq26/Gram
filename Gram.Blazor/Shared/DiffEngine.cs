namespace Gram.Blazor.Shared;

public static class DiffEngine
{
    /// <summary>
    /// Computes a line-by-line diff between original and new text using Longest Common Subsequence (LCS).
    /// </summary>
    public static DiffFileItem ComputeDiff(string filePath, string oldText, string newText)
    {
        var oldLines = SplitLines(oldText ?? string.Empty);
        var newLines = SplitLines(newText ?? string.Empty);

        var n = oldLines.Count;
        var m = newLines.Count;

        // DP table for LCS
        var dp = new int[n + 1, m + 1];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < m; j++)
            {
                if (oldLines[i] == newLines[j])
                {
                    dp[i + 1, j + 1] = dp[i, j] + 1;
                }
                else
                {
                    dp[i + 1, j + 1] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
                }
            }
        }

        // Backtrack to build diff lines
        var diffLines = new List<DiffLine>();
        var currI = n;
        var currJ = m;

        while (currI > 0 || currJ > 0)
        {
            if (currI > 0 && currJ > 0 && oldLines[currI - 1] == newLines[currJ - 1])
            {
                diffLines.Add(new DiffLine
                {
                    Type = "context",
                    Content = oldLines[currI - 1],
                    OldLineNumber = currI,
                    NewLineNumber = currJ
                });
                currI--;
                currJ--;
            }
            else if (currJ > 0 && (currI == 0 || dp[currI, currJ - 1] >= dp[currI - 1, currJ]))
            {
                diffLines.Add(new DiffLine
                {
                    Type = "add",
                    Content = newLines[currJ - 1],
                    OldLineNumber = null,
                    NewLineNumber = currJ
                });
                currJ--;
            }
            else if (currI > 0 && (currJ == 0 || dp[currI, currJ - 1] < dp[currI - 1, currJ]))
            {
                diffLines.Add(new DiffLine
                {
                    Type = "delete",
                    Content = oldLines[currI - 1],
                    OldLineNumber = currI,
                    NewLineNumber = null
                });
                currI--;
            }
        }

        diffLines.Reverse();

        var status = (oldLines.Count == 0 && newLines.Count > 0)
            ? "Added"
            : (newLines.Count == 0 && oldLines.Count > 0)
                ? "Deleted"
                : "Modified";

        return new DiffFileItem
        {
            FilePath = filePath,
            Status = status,
            Lines = diffLines,
            Hunks = ExtractHunks(diffLines)
        };
    }

    /// <summary>
    /// Parses a standard unified diff string (e.g. from git diff or [diff_block_start]...[diff_block_end]).
    /// </summary>
    public static DiffFileItem? ParseUnifiedDiff(string diffText, string defaultFilePath = "")
    {
        if (string.IsNullOrWhiteSpace(diffText)) return null;

        var lines = SplitLines(diffText);
        var diffLines = new List<DiffLine>();
        var filePath = defaultFilePath;

        int oldLine = 1;
        int newLine = 1;
        var insideHunk = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("--- ") || line.StartsWith("+++ "))
            {
                if (line.StartsWith("+++ "))
                {
                    var p = line[4..].Trim();
                    if (p.StartsWith("b/")) p = p[2..];
                    if (!string.IsNullOrWhiteSpace(p)) filePath = p;
                }
                continue;
            }

            if (line.StartsWith("@@"))
            {
                insideHunk = true;
                diffLines.Add(new DiffLine
                {
                    Type = "header",
                    Content = line,
                    OldLineNumber = null,
                    NewLineNumber = null
                });
                ParseHunkHeader(line, out oldLine, out _, out newLine, out _);
                continue;
            }

            if (!insideHunk)
            {
                if (line.StartsWith("diff --git"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        var targetPart = parts[3];
                        if (targetPart.StartsWith("b/")) targetPart = targetPart[2..];
                        filePath = targetPart;
                    }
                }
                continue;
            }

            if (line.StartsWith('+'))
            {
                diffLines.Add(new DiffLine
                {
                    Type = "add",
                    Content = line[1..],
                    OldLineNumber = null,
                    NewLineNumber = newLine++
                });
            }
            else if (line.StartsWith('-'))
            {
                diffLines.Add(new DiffLine
                {
                    Type = "delete",
                    Content = line[1..],
                    OldLineNumber = oldLine++,
                    NewLineNumber = null
                });
            }
            else
            {
                var text = line.StartsWith(' ') ? line[1..] : line;
                diffLines.Add(new DiffLine
                {
                    Type = "context",
                    Content = text,
                    OldLineNumber = oldLine++,
                    NewLineNumber = newLine++
                });
            }
        }

        if (diffLines.Count == 0) return null;

        return new DiffFileItem
        {
            FilePath = filePath,
            Status = "Modified",
            Lines = diffLines,
            Hunks = ExtractHunks(diffLines)
        };
    }

    public static List<DiffHunk> ExtractHunks(IReadOnlyList<DiffLine> lines)
    {
        var hunks = new List<DiffHunk>();
        if (lines == null || lines.Count == 0) return hunks;

        // If lines have standard unified diff headers, split by header
        if (lines.Any(l => l.Type == "header" || l.Content.StartsWith("@@")))
        {
            DiffHunk? current = null;
            var hunkIndex = 0;

            foreach (var line in lines)
            {
                if (line.Type == "header" || line.Content.StartsWith("@@"))
                {
                    if (current != null && current.Lines.Count > 0)
                    {
                        hunks.Add(current);
                    }
                    ParseHunkHeader(line.Content, out var oldStart, out var oldCnt, out var newStart, out var newCnt);
                    current = new DiffHunk
                    {
                        HunkIndex = hunkIndex++,
                        Header = line.Content,
                        OldStartLine = oldStart,
                        OldLineCount = oldCnt,
                        NewStartLine = newStart,
                        NewLineCount = newCnt,
                        Lines = []
                    };
                    continue;
                }

                if (current == null)
                {
                    current = new DiffHunk
                    {
                        HunkIndex = hunkIndex++,
                        Header = "@@ -1 +1 @@",
                        OldStartLine = line.OldLineNumber ?? 1,
                        OldLineCount = 0,
                        NewStartLine = line.NewLineNumber ?? 1,
                        NewLineCount = 0,
                        Lines = []
                    };
                }

                current.Lines.Add(line);
            }

            if (current != null && current.Lines.Count > 0)
            {
                hunks.Add(current);
            }

            return hunks;
        }

        // If no headers exist (e.g. raw LCS output from ComputeDiff), group changes separated by context lines
        var changeIndices = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Type == "add" || lines[i].Type == "delete")
            {
                changeIndices.Add(i);
            }
        }

        if (changeIndices.Count == 0) return hunks;

        // Cluster change indices where gap of context lines >= 2
        var clusters = new List<List<int>>();
        var currentCluster = new List<int> { changeIndices[0] };

        for (var i = 1; i < changeIndices.Count; i++)
        {
            var prev = changeIndices[i - 1];
            var curr = changeIndices[i];
            if (curr - prev - 1 >= 2)
            {
                clusters.Add(currentCluster);
                currentCluster = new List<int> { curr };
            }
            else
            {
                currentCluster.Add(curr);
            }
        }
        clusters.Add(currentCluster);

        var prevEnd = 0;
        for (var c = 0; c < clusters.Count; c++)
        {
            var cluster = clusters[c];
            var firstChange = cluster[0];
            var lastChange = cluster[^1];

            var startIdx = Math.Max(prevEnd, firstChange - 3);

            var nextFirstChange = (c + 1 < clusters.Count) ? clusters[c + 1][0] : -1;
            int endIdx;
            if (nextFirstChange >= 0)
            {
                var availableContext = nextFirstChange - lastChange - 1;
                var trailingContext = Math.Min(3, availableContext / 2);
                endIdx = lastChange + trailingContext;
            }
            else
            {
                endIdx = Math.Min(lines.Count - 1, lastChange + 3);
            }

            var hunkLines = new List<DiffLine>();
            for (var i = startIdx; i <= endIdx && i < lines.Count; i++)
            {
                hunkLines.Add(lines[i]);
            }

            var oldStart = hunkLines.FirstOrDefault(l => l.OldLineNumber.HasValue)?.OldLineNumber ?? 1;
            var newStart = hunkLines.FirstOrDefault(l => l.NewLineNumber.HasValue)?.NewLineNumber ?? 1;
            var oldCnt = hunkLines.Count(l => l.Type != "add");
            var newCnt = hunkLines.Count(l => l.Type != "delete");

            hunks.Add(new DiffHunk
            {
                HunkIndex = c,
                Header = $"@@ -{oldStart},{oldCnt} +{newStart},{newCnt} @@",
                OldStartLine = oldStart,
                OldLineCount = oldCnt,
                NewStartLine = newStart,
                NewLineCount = newCnt,
                Lines = hunkLines
            });

            prevEnd = endIdx + 1;
        }

        return hunks;
    }

    public static (bool Success, string NewContent) ApplyReverseHunk(string fileContent, DiffHunk hunk)
    {
        if (hunk == null || hunk.Lines.Count == 0) return (false, fileContent);

        var addedLines = hunk.Lines.Where(l => l.Type != "delete").Select(l => l.Content).ToList();
        var originalLines = hunk.Lines.Where(l => l.Type != "add").Select(l => l.Content).ToList();

        var fileLines = SplitLines(fileContent);

        // Try to locate the chunk corresponding to addedLines
        var searchStart = Math.Max(0, Math.Min(fileLines.Count - 1, hunk.NewStartLine - 1));
        var matchIndex = FindSubsequenceIndex(fileLines, addedLines, searchStart);
        if (matchIndex == -1)
        {
            matchIndex = FindSubsequenceIndex(fileLines, addedLines, 0);
        }

        if (matchIndex == -1)
        {
            return (false, fileContent);
        }

        fileLines.RemoveRange(matchIndex, addedLines.Count);
        fileLines.InsertRange(matchIndex, originalLines);

        var newLineEnding = fileContent.Contains("\r\n") ? "\r\n" : "\n";
        return (true, string.Join(newLineEnding, fileLines));
    }

    private static int FindSubsequenceIndex(List<string> source, List<string> target, int preferredStart)
    {
        if (target.Count == 0) return preferredStart;
        if (source.Count < target.Count) return -1;

        if (preferredStart <= source.Count - target.Count && MatchesAt(source, target, preferredStart))
        {
            return preferredStart;
        }

        for (int offset = 1; offset < source.Count; offset++)
        {
            var left = preferredStart - offset;
            if (left >= 0 && left <= source.Count - target.Count && MatchesAt(source, target, left))
            {
                return left;
            }

            var right = preferredStart + offset;
            if (right >= 0 && right <= source.Count - target.Count && MatchesAt(source, target, right))
            {
                return right;
            }
        }

        return -1;
    }

    private static bool MatchesAt(List<string> source, List<string> target, int index)
    {
        for (int i = 0; i < target.Count; i++)
        {
            if (source[index + i] != target[i]) return false;
        }
        return true;
    }

    public static string GenerateConventionalCommitMessage(IReadOnlyList<string> modifiedFiles, string? diffText = null)
    {
        if (modifiedFiles == null || modifiedFiles.Count == 0)
        {
            return "chore: update workspace files";
        }

        var names = modifiedFiles.Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n)).ToList();

        string type = "feat";
        string scope = "";

        if (modifiedFiles.All(f => f.Contains("test", StringComparison.OrdinalIgnoreCase) || f.Contains("spec", StringComparison.OrdinalIgnoreCase)))
        {
            type = "test";
            scope = "unit";
        }
        else if (modifiedFiles.All(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || f.Contains("doc", StringComparison.OrdinalIgnoreCase)))
        {
            type = "docs";
        }
        else if (modifiedFiles.All(f => f.EndsWith(".css", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
        {
            type = "feat";
            scope = "ui";
        }
        else if (diffText != null && (diffText.Contains("fix") || diffText.Contains("error") || diffText.Contains("bug") || diffText.Contains("catch")))
        {
            type = "fix";
        }

        if (string.IsNullOrEmpty(scope))
        {
            var firstDir = modifiedFiles.FirstOrDefault(f => f.Contains('/'))?.Split('/')[0];
            if (!string.IsNullOrEmpty(firstDir) && firstDir.Length < 16)
            {
                scope = firstDir.ToLowerInvariant();
            }
            else
            {
                var baseName = Path.GetFileNameWithoutExtension(modifiedFiles[0]);
                if (!string.IsNullOrEmpty(baseName)) scope = baseName.ToLowerInvariant();
            }
        }

        var summary = names.Count switch
        {
            1 => $"update {names[0]}",
            2 => $"update {names[0]} and {names[1]}",
            _ => $"update {names[0]} and {names.Count - 1} other files"
        };

        return !string.IsNullOrEmpty(scope) ? $"{type}({scope}): {summary}" : $"{type}: {summary}";
    }

    private static void ParseHunkHeader(string header, out int oldStart, out int oldCnt, out int newStart, out int newCnt)
    {
        oldStart = 1;
        oldCnt = 1;
        newStart = 1;
        newCnt = 1;
        try
        {
            var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                var oldPart = parts[1].TrimStart('-');
                var newPart = parts[2].TrimStart('+');

                var oldTokens = oldPart.Split(',');
                if (int.TryParse(oldTokens[0], out var os)) oldStart = os;
                if (oldTokens.Length > 1 && int.TryParse(oldTokens[1], out var oc)) oldCnt = oc;

                var newTokens = newPart.Split(',');
                if (int.TryParse(newTokens[0], out var ns)) newStart = ns;
                if (newTokens.Length > 1 && int.TryParse(newTokens[1], out var nc)) newCnt = nc;
            }
        }
        catch
        {
            // fallback defaults
        }
    }

    private static List<string> SplitLines(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;

        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            result.Add(line);
        }
        return result;
    }
}
