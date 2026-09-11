using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Gram.Blazor.Shared;

public sealed record CompilerDiagnostic(
    string? FilePath,
    int? Line,
    int? Column,
    string? Code,
    string Severity,
    string Message,
    string RawLine)
{
    public string Formatted => !string.IsNullOrWhiteSpace(RawLine)
        ? RawLine
        : $"{FilePath}({Line},{Column}): {Severity} {Code}: {Message}";
}

public static class CompilerDiagnosticParser
{
    // Roslyn / MSBuild: MyClass.cs(24,15): error CS1002: ; expected
    private static readonly Regex RoslynRegex = new(
        @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<severity>error|warning)\s*(?<code>[A-Z0-9]+):\s*(?<msg>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // TypeScript / ESLint: src/app.ts:15:3 - error TS2304: Cannot find name 'foo'.
    private static readonly Regex TypeScriptRegex = new(
        @"^(?<file>.+?):(?<line>\d+):(?<col>\d+)\s*-\s*(?<severity>error|warning)\s*(?<code>TS\d+):\s*(?<msg>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Rustc: error[E0425]: cannot find value `x` in this scope
    private static readonly Regex RustcRegex = new(
        @"^error\[(?<code>E\d+)\]:\s*(?<msg>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Python / Pytest: FAILED tests/test_calc.py::test_add - AssertionError: ...
    private static readonly Regex PytestRegex = new(
        @"^FAILED\s+(?<file>[^:]+)::(?<test>[^\s]+)\s*-\s*(?<msg>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // xUnit / VSTest: Failed MyNamespace.MyTestClass.MyTestMethod [2 ms]
    private static readonly Regex VSTestRegex = new(
        @"^(?:Failed|\[xUnit\.net[^\]]+\]\s+FAIL)\s+(?<test>[A-Za-z0-9_.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Go test: --- FAIL: TestName (0.00s)
    private static readonly Regex GoTestRegex = new(
        @"^---\s+FAIL:\s+(?<test>[A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VSTestStackRegex = new(
        @"in\s+(?<file>.+?):line\s+(?<line>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GoLineRegex = new(
        @"^(?<file>[^:\s]+\.go):(?<line>\d+):\s*(?<msg>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<CompilerDiagnostic> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];

        var results = new List<CompilerDiagnostic>();
        var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            var trimmed = rawLine.Trim();

            // 1. Roslyn / MSBuild
            var roslynMatch = RoslynRegex.Match(trimmed);
            if (roslynMatch.Success)
            {
                results.Add(new CompilerDiagnostic(
                    FilePath: roslynMatch.Groups["file"].Value.Replace('\\', '/'),
                    Line: int.TryParse(roslynMatch.Groups["line"].Value, out var l) ? l : null,
                    Column: int.TryParse(roslynMatch.Groups["col"].Value, out var c) ? c : null,
                    Code: roslynMatch.Groups["code"].Value,
                    Severity: roslynMatch.Groups["severity"].Value.ToUpperInvariant(),
                    Message: roslynMatch.Groups["msg"].Value.Trim(),
                    RawLine: trimmed));
                continue;
            }

            // 2. TypeScript
            var tsMatch = TypeScriptRegex.Match(trimmed);
            if (tsMatch.Success)
            {
                results.Add(new CompilerDiagnostic(
                    FilePath: tsMatch.Groups["file"].Value.Replace('\\', '/'),
                    Line: int.TryParse(tsMatch.Groups["line"].Value, out var l) ? l : null,
                    Column: int.TryParse(tsMatch.Groups["col"].Value, out var c) ? c : null,
                    Code: tsMatch.Groups["code"].Value,
                    Severity: tsMatch.Groups["severity"].Value.ToUpperInvariant(),
                    Message: tsMatch.Groups["msg"].Value.Trim(),
                    RawLine: trimmed));
                continue;
            }

            // 3. Rustc
            var rustMatch = RustcRegex.Match(trimmed);
            if (rustMatch.Success)
            {
                results.Add(new CompilerDiagnostic(
                    FilePath: null,
                    Line: null,
                    Column: null,
                    Code: rustMatch.Groups["code"].Value,
                    Severity: "ERROR",
                    Message: rustMatch.Groups["msg"].Value.Trim(),
                    RawLine: trimmed));
                continue;
            }

            // 4. Pytest
            var pyMatch = PytestRegex.Match(trimmed);
            if (pyMatch.Success)
            {
                results.Add(new CompilerDiagnostic(
                    FilePath: pyMatch.Groups["file"].Value.Replace('\\', '/'),
                    Line: null,
                    Column: null,
                    Code: pyMatch.Groups["test"].Value,
                    Severity: "ERROR",
                    Message: pyMatch.Groups["msg"].Value.Trim(),
                    RawLine: trimmed));
                continue;
            }

            // 5. xUnit / VSTest: Failed MyNamespace.MyTestClass.MyTestMethod [2 ms]
            var vsMatch = VSTestRegex.Match(trimmed);
            if (vsMatch.Success)
            {
                var testName = vsMatch.Groups["test"].Value;
                string? filePath = null;
                int? lineNum = null;
                string? detailMsg = null;

                for (int j = i + 1; j < Math.Min(lines.Length, i + 12); j++)
                {
                    var nextLine = lines[j].Trim();
                    if (nextLine.StartsWith("Failed ") || nextLine.StartsWith("--- FAIL")) break;

                    var stackMatch = VSTestStackRegex.Match(nextLine);
                    if (stackMatch.Success)
                    {
                        filePath = stackMatch.Groups["file"].Value.Replace('\\', '/');
                        if (int.TryParse(stackMatch.Groups["line"].Value, out var l)) lineNum = l;
                    }
                    else if (nextLine.StartsWith("Error Message:") && j + 1 < lines.Length)
                    {
                        detailMsg = lines[j + 1].Trim();
                    }
                }

                var msg = !string.IsNullOrWhiteSpace(detailMsg)
                    ? $"Test failed: {testName} - {detailMsg}"
                    : $"Test failed: {testName}";

                results.Add(new CompilerDiagnostic(
                    FilePath: filePath,
                    Line: lineNum,
                    Column: null,
                    Code: "TEST_FAILURE",
                    Severity: "ERROR",
                    Message: msg,
                    RawLine: trimmed));
                continue;
            }

            // 6. Go test: --- FAIL: TestName (0.00s)
            var goMatch = GoTestRegex.Match(trimmed);
            if (goMatch.Success)
            {
                var testName = goMatch.Groups["test"].Value;
                string? filePath = null;
                int? lineNum = null;
                string? detailMsg = null;

                for (int j = Math.Max(0, i - 5); j < Math.Min(lines.Length, i + 6); j++)
                {
                    if (j == i) continue;
                    var goFileMatch = GoLineRegex.Match(lines[j].Trim());
                    if (goFileMatch.Success)
                    {
                        filePath = goFileMatch.Groups["file"].Value.Replace('\\', '/');
                        if (int.TryParse(goFileMatch.Groups["line"].Value, out var l)) lineNum = l;
                        detailMsg = goFileMatch.Groups["msg"].Value.Trim();
                        break;
                    }
                }

                var msg = !string.IsNullOrWhiteSpace(detailMsg)
                    ? $"Go test failed: {testName} - {detailMsg}"
                    : $"Go test failed: {testName}";

                results.Add(new CompilerDiagnostic(
                    FilePath: filePath,
                    Line: lineNum,
                    Column: null,
                    Code: "TEST_FAILURE",
                    Severity: "ERROR",
                    Message: msg,
                    RawLine: trimmed));
                continue;
            }
        }

        return results;
    }

    public static string ComputeErrorHash(IEnumerable<CompilerDiagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase)).ToList();
        if (errors.Count == 0) return "0_no_errors";

        var sb = new StringBuilder();
        foreach (var err in errors.OrderBy(e => e.Code).ThenBy(e => e.FilePath).ThenBy(e => e.Line))
        {
            sb.Append($"{err.Code}|{err.FilePath}|{err.Line}|{err.Message};");
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes)[..16];
    }
}
