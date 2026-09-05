using System.Text;
using System.Text.RegularExpressions;

namespace Gram.Blazor.Shared;

public sealed record PatchResult(
    bool Success,
    string PatchedText,
    int AppliedTier,
    string? ErrorMessage = null,
    string? DiagnosticDetails = null);

public static class SurgicalPatchEngine
{
    public static PatchResult ApplyPatch(string originalText, string targetContent, string replacementContent)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(targetContent);
        ArgumentNullException.ThrowIfNull(replacementContent);

        if (targetContent.Length == 0)
        {
            return new PatchResult(
                Success: false,
                PatchedText: originalText,
                AppliedTier: 0,
                ErrorMessage: "Target content cannot be empty.");
        }

        // ==========================================
        // Tier 1: Exact Match
        // ==========================================
        var exactIndex = originalText.IndexOf(targetContent, StringComparison.Ordinal);
        if (exactIndex >= 0)
        {
            var patched = originalText[..exactIndex] + replacementContent + originalText[(exactIndex + targetContent.Length)..];
            return new PatchResult(
                Success: true,
                PatchedText: patched,
                AppliedTier: 1);
        }

        // ==========================================
        // Tier 2: Line-Ending & Whitespace/Indentation Tolerant Match
        // ==========================================
        var isOriginalCrlf = originalText.Contains("\r\n");
        var normalizedOriginal = originalText.Replace("\r\n", "\n");
        var normalizedTarget = targetContent.Replace("\r\n", "\n");
        var normalizedReplacement = replacementContent.Replace("\r\n", "\n");

        // Check if normalized line endings alone match
        var normIndex = normalizedOriginal.IndexOf(normalizedTarget, StringComparison.Ordinal);
        if (normIndex >= 0)
        {
            var replacedNorm = normalizedOriginal[..normIndex] + normalizedReplacement + normalizedOriginal[(normIndex + normalizedTarget.Length)..];
            var finalCrlfAware = isOriginalCrlf ? replacedNorm.Replace("\n", "\r\n") : replacedNorm;
            return new PatchResult(
                Success: true,
                PatchedText: finalCrlfAware,
                AppliedTier: 2,
                DiagnosticDetails: "Matched using line-ending normalization.");
        }

        // Indentation-tolerant line-by-line matching
        var origLines = normalizedOriginal.Split('\n');
        var targetLines = normalizedTarget.Split('\n');

        var matchLineIndex = FindWhitespaceTolerantMatch(origLines, targetLines);
        if (matchLineIndex >= 0)
        {
            var patchedLines = ApplyIndentationPreservingReplacement(origLines, targetLines, normalizedReplacement.Split('\n'), matchLineIndex);
            var joined = string.Join("\n", patchedLines);
            var finalResult = isOriginalCrlf ? joined.Replace("\n", "\r\n") : joined;
            return new PatchResult(
                Success: true,
                PatchedText: finalResult,
                AppliedTier: 2,
                DiagnosticDetails: $"Matched line-by-line starting at line {matchLineIndex + 1} with indentation preservation.");
        }

        // ==========================================
        // Tier 3: Anchor-Based Chunk Replacement
        // ==========================================
        var anchorResult = TryAnchorBasedReplacement(origLines, targetLines, normalizedReplacement.Split('\n'));
        if (anchorResult != null)
        {
            var joined = string.Join("\n", anchorResult.PatchedLines);
            var finalResult = isOriginalCrlf ? joined.Replace("\n", "\r\n") : joined;
            return new PatchResult(
                Success: true,
                PatchedText: finalResult,
                AppliedTier: 3,
                DiagnosticDetails: $"Matched via boundary anchors (Start line: {anchorResult.StartLineIndex + 1}, End line: {anchorResult.EndLineIndex + 1}).");
        }

        // ==========================================
        // All Tiers Failed: Diagnostic Failure Report
        // ==========================================
        var failureDiagnosis = BuildFailureDiagnosis(origLines, targetLines);
        return new PatchResult(
            Success: false,
            PatchedText: originalText,
            AppliedTier: 0,
            ErrorMessage: $"Target content was not found in file.\n{failureDiagnosis}",
            DiagnosticDetails: failureDiagnosis);
    }

    private static int FindWhitespaceTolerantMatch(string[] origLines, string[] targetLines)
    {
        if (targetLines.Length == 0 || origLines.Length < targetLines.Length) return -1;

        var targetTrimmed = targetLines.Select(t => t.Trim()).ToArray();

        for (int i = 0; i <= origLines.Length - targetLines.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < targetLines.Length; j++)
            {
                var origTrim = origLines[i + j].Trim();
                if (!origTrim.Equals(targetTrimmed[j], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match) return i;
        }

        return -1;
    }

    private static List<string> ApplyIndentationPreservingReplacement(
        string[] origLines,
        string[] targetLines,
        string[] replacementLines,
        int matchIndex)
    {
        var result = new List<string>(origLines.Length + replacementLines.Length);
        for (int i = 0; i < matchIndex; i++)
        {
            result.Add(origLines[i]);
        }

        // Measure leading whitespace of first target match
        var originalIndent = GetLeadingWhitespace(origLines[matchIndex]);
        var targetIndent = GetLeadingWhitespace(targetLines[0]);

        foreach (var replLine in replacementLines)
        {
            if (string.IsNullOrWhiteSpace(replLine))
            {
                result.Add(replLine);
                continue;
            }

            var replIndent = GetLeadingWhitespace(replLine);
            if (replIndent.StartsWith(targetIndent))
            {
                var relative = replIndent[targetIndent.Length..];
                result.Add(originalIndent + relative + replLine.TrimStart());
            }
            else
            {
                result.Add(replLine);
            }
        }

        for (int i = matchIndex + targetLines.Length; i < origLines.Length; i++)
        {
            result.Add(origLines[i]);
        }

        return result;
    }

    private sealed record AnchorMatchResult(List<string> PatchedLines, int StartLineIndex, int EndLineIndex);

    private static AnchorMatchResult? TryAnchorBasedReplacement(
        string[] origLines,
        string[] targetLines,
        string[] replacementLines)
    {
        if (targetLines.Length < 3) return null;

        var firstNonEmptyTarget = targetLines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
        var lastNonEmptyTarget = targetLines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();

        if (string.IsNullOrWhiteSpace(firstNonEmptyTarget) || string.IsNullOrWhiteSpace(lastNonEmptyTarget))
            return null;

        var startMatches = new List<int>();
        var endMatches = new List<int>();

        for (int i = 0; i < origLines.Length; i++)
        {
            var trimmed = origLines[i].Trim();
            if (trimmed.Equals(firstNonEmptyTarget, StringComparison.Ordinal)) startMatches.Add(i);
            if (trimmed.Equals(lastNonEmptyTarget, StringComparison.Ordinal)) endMatches.Add(i);
        }

        // If start and end anchors match uniquely
        if (startMatches.Count == 1 && endMatches.Count == 1)
        {
            var start = startMatches[0];
            var end = endMatches[0];
            if (start <= end && (end - start + 1) >= (targetLines.Length - 5) && (end - start + 1) <= (targetLines.Length + 10))
            {
                var result = new List<string>();
                for (int i = 0; i < start; i++) result.Add(origLines[i]);
                result.AddRange(replacementLines);
                for (int i = end + 1; i < origLines.Length; i++) result.Add(origLines[i]);
                return new AnchorMatchResult(result, start, end);
            }
        }

        return null;
    }

    private static string GetLeadingWhitespace(string line)
    {
        var match = Regex.Match(line, @"^\s*");
        return match.Success ? match.Value : "";
    }

    private static string BuildFailureDiagnosis(string[] origLines, string[] targetLines)
    {
        var sb = new StringBuilder();
        var firstTarget = targetLines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();

        if (firstTarget != null)
        {
            var closeMatches = new List<(int Line, string Text)>();
            for (int i = 0; i < origLines.Length; i++)
            {
                var lineTrim = origLines[i].Trim();
                if (lineTrim.Contains(firstTarget, StringComparison.OrdinalIgnoreCase) ||
                    firstTarget.Contains(lineTrim, StringComparison.OrdinalIgnoreCase) && lineTrim.Length > 8)
                {
                    closeMatches.Add((i + 1, origLines[i]));
                }
            }

            if (closeMatches.Count > 0)
            {
                sb.AppendLine("Closest matching lines in file:");
                foreach (var (line, text) in closeMatches.Take(3))
                {
                    sb.AppendLine($"  Line {line}: {text.Trim()}");
                }
            }
            else
            {
                sb.AppendLine($"First target line '{firstTarget}' was not found anywhere in the file.");
            }
        }

        return sb.ToString().Trim();
    }
}
