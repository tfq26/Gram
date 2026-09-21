using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class AutonomousTestRunnerTests
{
    [Fact]
    public void TestResultParser_ParsesDotNetSummaryAndFailures()
    {
        var dotNetOutput = @"
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.10]   Starting:    Gram.Blazor.UnitTests
Failed Gram.Blazor.UnitTests.CalculatorTests.TestDivideByZero [12 ms]
  Error Message:
   Assert.Throws() Failure: No exception was thrown
  Stack Trace:
     at Gram.Blazor.UnitTests.CalculatorTests.TestDivideByZero() in /Users/dev/CalculatorTests.cs:line 42

Passed!  - Failed:     1, Passed:   465, Skipped:     0, Total:   466, Duration: 11 s - Gram.Blazor.UnitTests.dll (net9.0)
";
        var summary = TestResultParser.Parse(dotNetOutput, exitCode: 1);

        Assert.False(summary.Success);
        Assert.Equal(465, summary.Passed);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(466, summary.Total);
        Assert.NotEmpty(summary.Failures);

        var fail = summary.Failures[0];
        Assert.Contains("CalculatorTests", fail.TestName);
        Assert.Contains("CalculatorTests.cs", fail.FilePath);
        Assert.Equal(42, fail.Line);
    }

    [Fact]
    public void TestResultParser_ParsesJestAndVitestFailures()
    {
        var jestOutput = @"
PASS src/components/Button.test.tsx
FAIL src/services/AuthService.test.ts
  ● AuthService › login with invalid password should fail

    expect(received).toBe(expected) // Object.is equality

    Expected: false
    Received: true

      14 |     const result = await authService.login('alice', 'wrong');
      15 |     expect(result.success).toBe(false);
      16 |   });
      at Object.<anonymous> (src/services/AuthService.test.ts:15:28)

Tests:       1 failed, 15 passed, 16 total
Snapshots:   0 total
Time:        2.341 s
";
        var summary = TestResultParser.Parse(jestOutput, exitCode: 1);

        Assert.False(summary.Success);
        Assert.Equal(15, summary.Passed);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(16, summary.Total);
        Assert.NotEmpty(summary.Failures);

        var fail = summary.Failures[0];
        Assert.Contains("AuthService", fail.TestName);
        Assert.Contains("AuthService.test.ts", fail.FilePath);
        Assert.Equal(15, fail.Line);
    }

    [Fact]
    public void TestResultParser_ParsesPytestFailures()
    {
        var pytestOutput = @"
============================= test session starts ==============================
collected 12 items

tests/test_math.py ....F.......                                          [100%]

=================================== FAILURES ===================================
FAILED tests/test_math.py::test_multiply - AssertionError: 4 != 5
========================= 1 failed, 11 passed in 0.32s =========================
";
        var summary = TestResultParser.Parse(pytestOutput, exitCode: 1);

        Assert.False(summary.Success);
        Assert.Equal(11, summary.Passed);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(12, summary.Total);
        Assert.NotEmpty(summary.Failures);
        Assert.Contains("test_math.py", summary.Failures[0].FilePath);
    }

    [Fact]
    public void TestResultParser_ParsesCargoTestFailures()
    {
        var cargoOutput = @"
running 10 tests
test tests::test_sanity ... ok
test tests::test_hash_integrity ... FAILED
test tests::test_encryption ... ok

failures:

---- tests::test_hash_integrity stdout ----
thread 'tests::test_hash_integrity' panicked at src/crypto.rs:88:9:
assertion `left == right` failed
  left: ""abc""
 right: ""xyz""

failures:
    tests::test_hash_integrity

test result: FAILED. 9 passed; 1 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.04s
";
        var summary = TestResultParser.Parse(cargoOutput, exitCode: 1);

        Assert.False(summary.Success);
        Assert.Equal(9, summary.Passed);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(10, summary.Total);
        Assert.NotEmpty(summary.Failures);
        Assert.Contains("test_hash_integrity", summary.Failures[0].TestName);
    }

    [Fact]
    public void ResolveTestCommandWithFilter_ResolvesEcosystemCommandsCorrectly()
    {
        var dotnet = new DetectedProjectValidation(ProjectEcosystem.DotNet, "dotnet build", "dotnet test", "App.sln", "App.sln");
        Assert.Equal("dotnet test --filter \"FullyQualifiedName~OrderTests\" --no-restore",
            ProjectValidationDetector.ResolveTestCommandWithFilter(dotnet, filter: "OrderTests"));

        var node = new DetectedProjectValidation(ProjectEcosystem.NodeJs, "npm run build", "npm test", "package.json", "package.json");
        Assert.Equal("npm test -- -t \"AuthService\"",
            ProjectValidationDetector.ResolveTestCommandWithFilter(node, filter: "AuthService"));

        var cargo = new DetectedProjectValidation(ProjectEcosystem.Rust, "cargo check", "cargo test", "Cargo.toml", "Cargo.toml");
        Assert.Equal("cargo test test_parsing",
            ProjectValidationDetector.ResolveTestCommandWithFilter(cargo, filter: "test_parsing"));

        var py = new DetectedProjectValidation(ProjectEcosystem.Python, "python -m py_compile", "pytest", "pytest.ini", "pytest.ini");
        Assert.Equal("pytest -k \"test_order\"",
            ProjectValidationDetector.ResolveTestCommandWithFilter(py, filter: "test_order"));

        var go = new DetectedProjectValidation(ProjectEcosystem.Go, "go build ./...", "go test ./...", "go.mod", "go.mod");
        Assert.Equal("go test -v -run \"TestBilling\" ./...",
            ProjectValidationDetector.ResolveTestCommandWithFilter(go, filter: "TestBilling"));
    }

    [Fact]
    public void AgentOrchestrator_RecognizesRunTestsAsRepairRelevantTool()
    {
        Assert.True(AgentOrchestrator.IsRepairRelevantTool("run_tests"));
        Assert.True(AgentOrchestrator.IsRepairRelevantTool("validate_project"));
    }
}
