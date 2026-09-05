using System.Text.Json.Nodes;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SelfHealingValidationLoopTests
{
    [Fact]
    public void ProjectValidationDetector_DetectsDotNetProjects()
    {
        using var tempDir = new TemporaryDirectory();
        var csproj = Path.Combine(tempDir.Path, "MyApp.csproj");
        File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");

        var detected = ProjectValidationDetector.Detect(tempDir.Path);
        Assert.NotNull(detected);
        Assert.Equal(ProjectEcosystem.DotNet, detected.Ecosystem);
        Assert.Equal("dotnet build", detected.BuildCommand);
        Assert.Equal("dotnet test", detected.TestCommand);
    }

    [Fact]
    public void ProjectValidationDetector_DetectsNodeProjects()
    {
        using var tempDir = new TemporaryDirectory();
        var pkgJson = Path.Combine(tempDir.Path, "package.json");
        var json = new JsonObject
        {
            ["name"] = "test-node",
            ["scripts"] = new JsonObject
            {
                ["test"] = "jest",
                ["build"] = "tsc"
            }
        }.ToJsonString();
        File.WriteAllText(pkgJson, json);

        var detected = ProjectValidationDetector.Detect(tempDir.Path);
        Assert.NotNull(detected);
        Assert.Equal(ProjectEcosystem.NodeJs, detected.Ecosystem);
        Assert.Equal("npm run build", detected.BuildCommand);
        Assert.Equal("npm test", detected.TestCommand);
    }

    [Fact]
    public void ProjectValidationDetector_DetectsRustAndPythonProjects()
    {
        using var rustDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(rustDir.Path, "Cargo.toml"), "[package]\nname = \"my_crate\"");
        var rustDetected = ProjectValidationDetector.Detect(rustDir.Path);
        Assert.NotNull(rustDetected);
        Assert.Equal(ProjectEcosystem.Rust, rustDetected.Ecosystem);
        Assert.Equal("cargo check", rustDetected.BuildCommand);

        using var pyDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(pyDir.Path, "pytest.ini"), "[pytest]\npython_files = tests.py");
        var pyDetected = ProjectValidationDetector.Detect(pyDir.Path);
        Assert.NotNull(pyDetected);
        Assert.Equal(ProjectEcosystem.Python, pyDetected.Ecosystem);
        Assert.Equal("pytest", pyDetected.TestCommand);
    }

    [Fact]
    public void CompilerDiagnosticParser_ParsesRoslynAndTypeScriptDiagnostics()
    {
        var roslynOutput = @"
C:\Users\foo\Program.cs(42,10): error CS1002: ; expected
C:\Users\foo\Program.cs(45,12): warning CS0168: The variable 'ex' is declared but never used
";
        var roslynDiagnostics = CompilerDiagnosticParser.Parse(roslynOutput);
        Assert.Equal(2, roslynDiagnostics.Count);
        Assert.Equal("ERROR", roslynDiagnostics[0].Severity);
        Assert.Equal("CS1002", roslynDiagnostics[0].Code);
        Assert.Equal(42, roslynDiagnostics[0].Line);
        Assert.Equal(10, roslynDiagnostics[0].Column);

        Assert.Equal("WARNING", roslynDiagnostics[1].Severity);
        Assert.Equal("CS0168", roslynDiagnostics[1].Code);

        var tsOutput = "src/index.ts:12:5 - error TS2304: Cannot find name 'myVar'.";
        var tsDiagnostics = CompilerDiagnosticParser.Parse(tsOutput);
        Assert.Single(tsDiagnostics);
        Assert.Equal("ERROR", tsDiagnostics[0].Severity);
        Assert.Equal("TS2304", tsDiagnostics[0].Code);
        Assert.Equal(12, tsDiagnostics[0].Line);
    }

    [Fact]
    public void SelfHealingCoordinator_BreaksLoopOnRepeatedErrorHash()
    {
        var coordinator = new SelfHealingLoopCoordinator(new SelfHealingOptions { MaxAttempts = 3 });

        var failingOutput = "Program.cs(10,5): error CS1002: ; expected";

        // Iteration 1: records error, should continue
        var passed1 = coordinator.RecordValidationRun(failingOutput, exitCode: 1, out var haltReason1);
        Assert.False(passed1);
        Assert.Null(haltReason1);
        Assert.True(coordinator.ShouldContinue(out _));

        // Iteration 2: same error returns -> repetition loop breaker triggers!
        var passed2 = coordinator.RecordValidationRun(failingOutput, exitCode: 1, out var haltReason2);
        Assert.False(passed2);
        Assert.NotNull(haltReason2);
        Assert.Contains("Same compiler error repeated across iterations", haltReason2);
        Assert.False(coordinator.ShouldContinue(out var stopReason));
        Assert.Contains("repeated compiler error", stopReason);
    }

    [Fact]
    public void SelfHealingCoordinator_HaltsWhenMaxAttemptsReached()
    {
        var coordinator = new SelfHealingLoopCoordinator(new SelfHealingOptions { MaxAttempts = 2 });

        coordinator.RecordValidationRun("Program.cs(10,5): error CS1002: ; expected", 1, out _);
        var passed = coordinator.RecordValidationRun("Program.cs(20,5): error CS0103: The name 'y' does not exist", 1, out var haltReason);

        Assert.False(passed);
        Assert.NotNull(haltReason);
        Assert.Contains("maximum repair budget", haltReason);
        Assert.False(coordinator.ShouldContinue(out _));
    }
}
