using System.Text.Json.Nodes;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class AutonomousCodingEnhancementsTests
{
    [Fact]
    public async Task NativeProcessRunner_LiveStreaming_InvokesLineHandler()
    {
        var runner = new NativeProcessRunner();
        using var tempDir = new TemporaryDirectory();

        var streamedLines = new List<string>();
        var testCmd = OperatingSystem.IsWindows()
            ? "Write-Output 'Line 1'; Write-Output 'Line 2'"
            : "echo 'Line 1'; echo 'Line 2'";

        var result = await runner.ExecuteAsync(new ProcessExecutionOptions
        {
            CommandLine = testCmd,
            WorkingDirectory = tempDir.Path,
            WorkspaceRoot = tempDir.Path,
            TimeoutSeconds = 15,
            OutputLineHandler = line =>
            {
                lock (streamedLines)
                {
                    streamedLines.Add(line);
                }
            }
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(streamedLines, l => l.Contains("Line 1"));
        Assert.Contains(streamedLines, l => l.Contains("Line 2"));
    }

    [Fact]
    public void CompilerDiagnosticParser_ParsesVSTestFailures()
    {
        var rawOutput = @"
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
  Failed MyProject.Tests.CalculatorTests.Add_Fails [12 ms]
  Error Message:
   Assert.Equal() Failure
   Expected: 5
   Actual:   4
  Stack Trace:
     at MyProject.Tests.CalculatorTests.Add_Fails() in /src/CalculatorTests.cs:line 42

Failed! - Failed: 1, Passed: 10, Skipped: 0, Total: 11, Duration: 450 ms
";

        var diagnostics = CompilerDiagnosticParser.Parse(rawOutput);
        Assert.NotEmpty(diagnostics);

        var failDiag = diagnostics.FirstOrDefault(d => d.Code == "TEST_FAILURE");
        Assert.NotNull(failDiag);
        Assert.Equal("ERROR", failDiag.Severity);
        Assert.Contains("CalculatorTests.Add_Fails", failDiag.Message);
        Assert.Equal("/src/CalculatorTests.cs", failDiag.FilePath);
        Assert.Equal(42, failDiag.Line);
    }

    [Fact]
    public void CompilerDiagnosticParser_ParsesGoTestFailures()
    {
        var rawOutput = @"
=== RUN   TestCompute
    compute_test.go:28: expected 100, got 99
--- FAIL: TestCompute (0.01s)
FAIL
";

        var diagnostics = CompilerDiagnosticParser.Parse(rawOutput);
        Assert.NotEmpty(diagnostics);

        var failDiag = diagnostics.FirstOrDefault(d => d.Code == "TEST_FAILURE");
        Assert.NotNull(failDiag);
        Assert.Equal("ERROR", failDiag.Severity);
        Assert.Equal("compute_test.go", failDiag.FilePath);
        Assert.Equal(28, failDiag.Line);
        Assert.Contains("expected 100, got 99", failDiag.Message);
    }

    [Theory]
    [InlineData("dotnet", "CalculatorTests.cs", "dotnet test --filter \"FullyQualifiedName~CalculatorTests\" --no-restore -clp:NoSummary -v:q")]
    [InlineData("typescript", "user.spec.ts", "npm test --if-present -- -t \"user.spec\"")]
    [InlineData("cargo", "engine.rs", "cargo test engine")]
    [InlineData("go", "handler_test.go", "go test -run handler_test ./...")]
    [InlineData("python", "test_auth.py", "python3 -m unittest discover -p \"*test_auth*\" -q")]
    public void WorkspaceDiagnosticService_ResolveTargetedTestCommand_Ecosystems(
        string toolchain, string modifiedFile, string expectedCommand)
    {
        var cmd = WorkspaceDiagnosticService.ResolveTargetedTestCommand(toolchain, "/workspace", new[] { modifiedFile });
        Assert.NotNull(cmd);
        Assert.Equal(expectedCommand, cmd);
    }

    [Fact]
    public void RepositoryMapService_ExtractSemanticSymbols_IdentifiesClassesAndMethods()
    {
        var sourceCode = @"
namespace Demo;

public interface ICalculator
{
    int Add(int a, int b);
}

public sealed class MathService : ICalculator
{
    public int Add(int a, int b)
    {
        return a + b;
    }

    public async Task<string> FormatAsync(int val)
    {
        return val.ToString();
    }
}

public record ComputeResult(int Value, bool Success);
";

        var symbols = RepositoryMapService.ExtractSemanticSymbols(sourceCode, "src/MathService.cs");

        Assert.Contains(symbols, s => s.Name == "ICalculator" && s.Kind == "interface" && s.LineNumber == 4);
        Assert.Contains(symbols, s => s.Name == "MathService" && s.Kind == "class" && s.LineNumber == 9);
        Assert.Contains(symbols, s => s.Name == "Add" && s.Kind == "method" && s.LineNumber == 11);
        Assert.Contains(symbols, s => s.Name == "FormatAsync" && s.Kind == "method" && s.LineNumber == 16);
        Assert.Contains(symbols, s => s.Name == "ComputeResult" && s.Kind == "record" && s.LineNumber == 22);
    }

    [Fact]
    public async Task RepositoryMapService_BuildDependencyGraph_PopulatesSemanticSymbols()
    {
        using var tempDir = new TemporaryDirectory();
        var srcDir = Path.Combine(tempDir.Path, "src");
        Directory.CreateDirectory(srcDir);

        var filePath = Path.Combine(srcDir, "Greeter.cs");
        await File.WriteAllTextAsync(filePath, @"
namespace App;

public class Greeter
{
    public string SayHello(string name) => $""Hello {name}"";
}
");

        var repoMap = new RepositoryMapService();
        var graph = await repoMap.BuildDependencyGraphAsync(tempDir.Path);

        Assert.NotNull(graph);
        Assert.NotEmpty(graph.SemanticSymbols);

        var greeterClass = graph.SemanticSymbols.FirstOrDefault(s => s.Name == "Greeter");
        Assert.NotNull(greeterClass);
        Assert.Equal("class", greeterClass.Kind);

        var def = await repoMap.FindDefinitionAsync(tempDir.Path, "Greeter");
        Assert.NotNull(def);
        Assert.Equal("Greeter", def.Name);
        Assert.Equal(4, def.LineNumber);
    }

    [Fact]
    public async Task AgentTools_FindSymbolDefinition_ReturnsDefinitionResult()
    {
        using var tempDir = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempDir.Path);

        var srcDir = Path.Combine(tempDir.Path, "src");
        Directory.CreateDirectory(srcDir);

        var filePath = Path.Combine(srcDir, "OrderProcessor.cs");
        await File.WriteAllTextAsync(filePath, @"
namespace ECommerce;

public class OrderProcessor
{
    public void ProcessOrder(int orderId)
    {
    }
}
");

        var storage = new StorageService();
        var gitService = new GitService(storage);
        var securityService = new AgentSecurityService(storage);
        var toolboxService = new ToolboxManagerService(storage);
        var toolKernel = new GramToolKernel(securityService, toolboxService, Enumerable.Empty<IGramTool>());
        var webContent = new WebContentService(new SimpleFactory());
        var repoMap = new RepositoryMapService();

        var registry = new AgentToolRegistry(
            gitService,
            securityService,
            toolboxService,
            toolKernel,
            webContent,
            repoMapService: repoMap);

        // Pre-build index
        await repoMap.BuildDependencyGraphAsync(tempDir.Path);

        var args = new JsonObject
        {
            ["symbol"] = "OrderProcessor"
        }.ToJsonString();

        var result = await registry.ExecuteToolAsync("find_symbol_definition", "call-sym-1", args, tempDir.Path);

        Assert.False(result.IsError, result.Output);
        Assert.Contains("Found [class] 'OrderProcessor'", result.Output);
        Assert.Contains("OrderProcessor.cs:4", result.Output);
    }

    [Fact]
    public async Task AgentTools_FindSymbolDefinition_MissingSymbol_ReturnsError()
    {
        using var tempDir = new TemporaryDirectory();
        var storage = new StorageService();
        var gitService = new GitService(storage);
        var securityService = new AgentSecurityService(storage);
        var toolboxService = new ToolboxManagerService(storage);
        var toolKernel = new GramToolKernel(securityService, toolboxService, Enumerable.Empty<IGramTool>());
        var webContent = new WebContentService(new SimpleFactory());
        var repoMap = new RepositoryMapService();

        var registry = new AgentToolRegistry(
            gitService,
            securityService,
            toolboxService,
            toolKernel,
            webContent,
            repoMapService: repoMap);

        var args = new JsonObject().ToJsonString();
        var result = await registry.ExecuteToolAsync("find_symbol_definition", "call-sym-2", args, tempDir.Path);

        Assert.True(result.IsError);
        Assert.Contains("Missing required argument 'symbol'", result.Output);
    }

    [Fact]
    public void DiffEngine_ExtractHunks_IdentifiesMultipleHunks()
    {
        var rawDiff = @"--- a/src/app.cs
+++ b/src/app.cs
@@ -10,4 +10,5 @@
 context line 1
-deleted line A
+added line A1
+added line A2
 context line 2
@@ -40,3 +41,3 @@
 context line 3
-deleted line B
+added line B
 context line 4
";

        var diff = DiffEngine.ParseUnifiedDiff(rawDiff, "src/app.cs");
        Assert.NotNull(diff);
        Assert.Equal(2, diff.Hunks.Count);

        var hunk1 = diff.Hunks[0];
        Assert.Equal(10, hunk1.OldStartLine);
        Assert.Equal(2, hunk1.AdditionsCount);
        Assert.Equal(1, hunk1.DeletionsCount);

        var hunk2 = diff.Hunks[1];
        Assert.Equal(40, hunk2.OldStartLine);
        Assert.Equal(1, hunk2.AdditionsCount);
        Assert.Equal(1, hunk2.DeletionsCount);
    }

    [Fact]
    public void DiffEngine_ApplyReverseHunk_RevertsSpecificHunkOnly()
    {
        var originalText = "header\nline one\nline two\nmiddle\nline four\nline five\nfooter";
        var modifiedText = "header\nline ONE MODIFIED\nline two\nmiddle\nline FOUR MODIFIED\nline five\nfooter";

        var diff = DiffEngine.ComputeDiff("file.txt", originalText, modifiedText);
        Assert.NotNull(diff);
        Assert.True(diff.Hunks.Count >= 1);

        // Take the first hunk and reverse it
        var firstHunk = diff.Hunks[0];
        var (ok, updatedText) = DiffEngine.ApplyReverseHunk(modifiedText, firstHunk);

        Assert.True(ok);
        // The first hunk change should be reverted to original "line one"
        Assert.Contains("line one", updatedText);
        Assert.DoesNotContain("line ONE MODIFIED", updatedText);
        // But the second change "line FOUR MODIFIED" remains untouched!
        Assert.Contains("line FOUR MODIFIED", updatedText);
    }

    [Theory]
    [InlineData("tests/CalculatorTests.cs", "test", "unit")]
    [InlineData("docs/architecture.md", "docs", "")]
    [InlineData("Components/Modal.razor", "feat", "ui")]
    public void DiffEngine_GenerateConventionalCommitMessage_GeneratesExpectedPatterns(
        string file, string expectedType, string expectedScope)
    {
        var msg = DiffEngine.GenerateConventionalCommitMessage(new[] { file });
        Assert.StartsWith(expectedType, msg);
        if (!string.IsNullOrEmpty(expectedScope))
        {
            Assert.Contains($"({expectedScope})", msg);
        }
    }

    [Fact]
    public void AgentOrchestrator_EnforceMaxTwoParagraphs_CapsAtTwoParagraphsWhenNotVerbose()
    {
        var input = "Paragraph 1: Implemented feature.\n\nParagraph 2: Added unit tests.\n\nParagraph 3: Extra rambling.\n\nParagraph 4: More rambling.";
        var result = AgentOrchestrator.EnforceMaxTwoParagraphs(input, allowVerbose: false);
        
        Assert.Contains("Paragraph 1: Implemented feature.", result);
        Assert.Contains("Paragraph 2: Added unit tests.", result);
        Assert.DoesNotContain("Paragraph 3: Extra rambling.", result);
        Assert.DoesNotContain("Paragraph 4: More rambling.", result);
    }

    [Fact]
    public void AgentOrchestrator_EnforceMaxTwoParagraphs_PreservesWhenVerboseRequested()
    {
        var input = "Paragraph 1.\n\nParagraph 2.\n\nParagraph 3.\n\nParagraph 4.";
        var result = AgentOrchestrator.EnforceMaxTwoParagraphs(input, allowVerbose: true);
        
        Assert.Equal(input, result);
    }

    [Theory]
    [InlineData("Please explain this in detail", true)]
    [InlineData("Be verbose with the architectural choices", true)]
    [InlineData("Give me a step-by-step walkthrough", true)]
    [InlineData("Fix the typo in Calculator.cs", false)]
    [InlineData("Build the tiered discount policy in GramLab", false)]
    public void AgentOrchestrator_UserRequestedVerbosity_DetectsIntentCorrectly(string prompt, bool expected)
    {
        var actual = AgentOrchestrator.UserRequestedVerbosity(prompt);
        Assert.Equal(expected, actual);
    }

    private sealed class SimpleFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
