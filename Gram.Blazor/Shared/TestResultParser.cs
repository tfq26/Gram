using System.Text.RegularExpressions;

namespace Gram.Blazor.Shared;

public sealed record TestFailure(
    string TestName,
    string? FilePath,
    int? Line,
    string Message,
    string? StackTrace = null);

public sealed record TestRunSummary(
    bool Success,
    int Passed,
    int Failed,
    int Skipped,
    int Total,
    IReadOnlyList<TestFailure> Failures,
    string SummaryLine);

public static class TestResultParser
{
    // .NET: Passed!  - Failed:     0, Passed:   466, Skipped:     0, Total:   466
    // or Failed!  - Failed:     2, Passed:   464, Skipped:     0, Total:   466
    private static readonly Regex DotNetSummaryRegex = new(
        @"(?:Passed|Failed)!\s+-\s+Failed:\s+(?<failed>\d+),\s+Passed:\s+(?<passed>\d+),\s+Skipped:\s+(?<skipped>\d+),\s+Total:\s+(?<total>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Jest / Vitest: Tests:       2 failed, 10 passed, 12 total
    private static readonly Regex JestSummaryRegex = new(
        @"Tests:\s+(?:(?<failed>\d+)\s+failed,?\s*)?(?:(?<skipped>\d+)\s+skipped,?\s*)?(?:(?<passed>\d+)\s+passed,?\s*)?(?<total>\d+)\s+total",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pytest: === 2 failed, 10 passed in 0.45s ===
    private static readonly Regex PytestSummaryRegex = new(
        @"=+\s+(?:(?<failed>\d+)\s+failed,?\s*)?(?:(?<passed>\d+)\s+passed,?\s*)?(?:(?<skipped>\d+)\s+skipped,?\s*)?in\s+[\d.]+s\s+=+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Cargo test: test result: FAILED. 10 passed; 2 failed; 0 ignored; 0 measured; 0 filtered out
    private static readonly Regex CargoSummaryRegex = new(
        @"test result:\s+(?<status>ok|FAILED)\.\s+(?<passed>\d+)\s+passed;\s+(?<failed>\d+)\s+failed;\s+(?<skipped>\d+)\s+ignored",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Jest / Vitest failure headers: ✕ testName or ● testName
    private static readonly Regex JestFailureHeaderRegex = new(
        @"^(?:✕|●|FAIL)\s+(?<name>.+)$",
        RegexOptions.Compiled);

    // Cargo failure headers: test test_name ... FAILED
    private static readonly Regex CargoFailureRegex = new(
        @"^test\s+(?<name>[^\s]+)\s+\.\.\.\s+FAILED",
        RegexOptions.Compiled);

    // Stack line: at Object.<anonymous> (src/file.ts:15:28) or in /file.cs:line 42
    private static readonly Regex StackLocationRegex = new(
        @"\b(?:at|in)\s+(?:.+?\()?(?<file>[A-Za-z0-9_./\\-]+\.[a-zA-Z0-9]+)(?::line\s+|:)(?<line>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TestRunSummary Parse(string rawOutput, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return new TestRunSummary(
                Success: exitCode == 0,
                Passed: 0,
                Failed: exitCode == 0 ? 0 : 1,
                Skipped: 0,
                Total: exitCode == 0 ? 0 : 1,
                Failures: exitCode == 0 ? [] : [new TestFailure("TestRunner", null, null, "Test execution exited with error code " + exitCode)],
                SummaryLine: exitCode == 0 ? "Test run completed with exit code 0." : "Test run failed with exit code " + exitCode);
        }

        var lines = rawOutput.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var failures = new List<TestFailure>();

        int passed = 0;
        int failed = 0;
        int skipped = 0;
        int total = 0;
        string summaryLine = "";

        // 1. Check .NET summary
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var match = DotNetSummaryRegex.Match(lines[i]);
            if (match.Success)
            {
                failed = int.Parse(match.Groups["failed"].Value);
                passed = int.Parse(match.Groups["passed"].Value);
                skipped = int.Parse(match.Groups["skipped"].Value);
                total = int.Parse(match.Groups["total"].Value);
                summaryLine = lines[i].Trim();
                break;
            }
        }

        // 2. Check Jest / Vitest summary if not dotnet
        if (total == 0)
        {
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var match = JestSummaryRegex.Match(lines[i]);
                if (match.Success)
                {
                    if (match.Groups["failed"].Success) failed = int.Parse(match.Groups["failed"].Value);
                    if (match.Groups["passed"].Success) passed = int.Parse(match.Groups["passed"].Value);
                    if (match.Groups["skipped"].Success) skipped = int.Parse(match.Groups["skipped"].Value);
                    total = int.Parse(match.Groups["total"].Value);
                    summaryLine = lines[i].Trim();
                    break;
                }
            }
        }

        // 3. Check Pytest summary if not matched
        if (total == 0)
        {
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var match = PytestSummaryRegex.Match(lines[i]);
                if (match.Success)
                {
                    if (match.Groups["failed"].Success) failed = int.Parse(match.Groups["failed"].Value);
                    if (match.Groups["passed"].Success) passed = int.Parse(match.Groups["passed"].Value);
                    if (match.Groups["skipped"].Success) skipped = int.Parse(match.Groups["skipped"].Value);
                    total = failed + passed + skipped;
                    summaryLine = lines[i].Trim();
                    break;
                }
            }
        }

        // 4. Check Cargo summary if not matched
        if (total == 0)
        {
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var match = CargoSummaryRegex.Match(lines[i]);
                if (match.Success)
                {
                    failed = int.Parse(match.Groups["failed"].Value);
                    passed = int.Parse(match.Groups["passed"].Value);
                    skipped = int.Parse(match.Groups["skipped"].Value);
                    total = failed + passed + skipped;
                    summaryLine = lines[i].Trim();
                    break;
                }
            }
        }

        // Extract individual failures from CompilerDiagnosticParser
        var diagnostics = CompilerDiagnosticParser.Parse(rawOutput);
        foreach (var diag in diagnostics.Where(d => d.Code == "TEST_FAILURE" || d.Severity == "ERROR"))
        {
            failures.Add(new TestFailure(
                TestName: diag.Code == "TEST_FAILURE" ? diag.Message.Split('-').FirstOrDefault()?.Trim() ?? diag.RawLine : (diag.Code ?? "Error"),
                FilePath: diag.FilePath,
                Line: diag.Line,
                Message: diag.Message,
                StackTrace: null));
        }

        // Parse any additional Jest / Vitest / Cargo failures
        string? currentFile = null;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.StartsWith("FAIL ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("PASS ", StringComparison.OrdinalIgnoreCase))
            {
                currentFile = line[5..].Trim().Replace('\\', '/');
            }

            // Jest failure
            var jestMatch = JestFailureHeaderRegex.Match(line);
            if (jestMatch.Success)
            {
                var testName = jestMatch.Groups["name"].Value.Trim();
                if (!failures.Any(f => f.TestName.Contains(testName, StringComparison.OrdinalIgnoreCase)))
                {
                    string? file = currentFile;
                    int? lineNum = null;
                    string msg = testName;

                    for (int j = i + 1; j < Math.Min(lines.Length, i + 25); j++)
                    {
                        var stackMatch = StackLocationRegex.Match(lines[j]);
                        if (stackMatch.Success)
                        {
                            file = stackMatch.Groups["file"].Value.Replace('\\', '/');
                            if (int.TryParse(stackMatch.Groups["line"].Value, out var l)) lineNum = l;
                            break;
                        }
                    }

                    failures.Add(new TestFailure(testName, file, lineNum, msg));
                }
            }

            // Cargo test failure
            var cargoMatch = CargoFailureRegex.Match(line);
            if (cargoMatch.Success)
            {
                var testName = cargoMatch.Groups["name"].Value.Trim();
                if (!failures.Any(f => f.TestName.Contains(testName, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add(new TestFailure(testName, null, null, $"Cargo test '{testName}' failed"));
                }
            }
        }

        if (total == 0)
        {
            total = failures.Count;
            failed = failures.Count;
            passed = exitCode == 0 ? 1 : 0;
            if (string.IsNullOrEmpty(summaryLine))
            {
                summaryLine = exitCode == 0 ? "All tests passed." : $"{failures.Count} test(s) failed.";
            }
        }

        var isSuccess = exitCode == 0 && failed == 0;

        return new TestRunSummary(
            Success: isSuccess,
            Passed: passed,
            Failed: failed,
            Skipped: skipped,
            Total: total,
            Failures: failures,
            SummaryLine: summaryLine);
    }
}
