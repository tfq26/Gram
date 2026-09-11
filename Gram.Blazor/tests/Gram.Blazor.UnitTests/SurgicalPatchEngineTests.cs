using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SurgicalPatchEngineTests
{
    [Fact]
    public void PatchEngine_Tier1_ExactMatch_AppliesSuccessfully()
    {
        var original = "public void Test() {\n    Console.WriteLine(\"Old\");\n}";
        var target = "Console.WriteLine(\"Old\");";
        var replacement = "Console.WriteLine(\"New\");";

        var result = SurgicalPatchEngine.ApplyPatch(original, target, replacement);

        Assert.True(result.Success);
        Assert.Equal(1, result.AppliedTier);
        Assert.Contains("Console.WriteLine(\"New\");", result.PatchedText);
        Assert.DoesNotContain("Console.WriteLine(\"Old\");", result.PatchedText);
    }

    [Fact]
    public void PatchEngine_Tier2_LineEndingNormalized_AppliesSuccessfully()
    {
        var original = "line1\r\nline2\r\nline3";
        // Target uses \n instead of \r\n
        var target = "line2\nline3";
        var replacement = "line2_modified\nline3_modified";

        var result = SurgicalPatchEngine.ApplyPatch(original, target, replacement);

        Assert.True(result.Success);
        Assert.Equal(2, result.AppliedTier);
        Assert.Contains("line2_modified\r\nline3_modified", result.PatchedText);
    }

    [Fact]
    public void PatchEngine_Tier2_WhitespaceTolerantMatch_PreservesOriginalIndentation()
    {
        var original = "class Foo\n{\n        public void Bar()\n        {\n            var x = 1;\n        }\n}";
        // Target has different leading indentation (4 spaces instead of 8)
        var target = "    public void Bar()\n    {\n        var x = 1;\n    }";
        var replacement = "    public void Bar()\n    {\n        var x = 99;\n    }";

        var result = SurgicalPatchEngine.ApplyPatch(original, target, replacement);

        Assert.True(result.Success);
        Assert.Equal(2, result.AppliedTier);
        Assert.Contains("public void Bar()", result.PatchedText);
        Assert.Contains("var x = 99;", result.PatchedText);
    }

    [Fact]
    public void PatchEngine_Tier3_AnchorBasedReplacement_ReplacesBlockBetweenAnchors()
    {
        var original = "void Start()\n{\n    // header comment\n    var a = 1;\n    var b = 2;\n    var c = 3;\n    // footer comment\n}";
        // Target specifies start anchor line and end anchor line, with intermediate line discrepancy
        var target = "    // header comment\n    var a = 9999;\n    // footer comment";
        var replacement = "    // header comment\n    var updated = true;\n    // footer comment";

        var result = SurgicalPatchEngine.ApplyPatch(original, target, replacement);

        Assert.True(result.Success);
        Assert.Equal(3, result.AppliedTier);
        Assert.Contains("var updated = true;", result.PatchedText);
        Assert.DoesNotContain("var a = 1;", result.PatchedText);
    }

    [Fact]
    public void PatchEngine_FailsGracefully_WithDiagnosticHint()
    {
        var original = "int alpha = 100;\nint beta = 200;\nint gamma = 300;";
        var target = "int nonExistent = 999;\nint missing = 888;";
        var replacement = "int replaced = 0;";

        var result = SurgicalPatchEngine.ApplyPatch(original, target, replacement);

        Assert.False(result.Success);
        Assert.Equal(0, result.AppliedTier);
        Assert.Contains("Target content was not found", result.ErrorMessage);
        Assert.Contains("nonExistent", result.ErrorMessage);
    }

    [Fact]
    public void PatchEngine_WithLineAnchors_ReplacesSpecificDuplicateOccurrence()
    {
        var lines = new List<string>();
        for (int i = 1; i <= 20; i++)
        {
            if (i == 5 || i == 15)
                lines.Add("    Console.WriteLine(\"TargetDuplicate\");");
            else
                lines.Add($"    int step_{i} = {i};");
        }
        var original = string.Join("\n", lines);

        // Replace only the occurrence around line 15 by specifying startLine: 12, endLine: 18
        var target = "Console.WriteLine(\"TargetDuplicate\");";
        var replacement = "Console.WriteLine(\"ReplacementFifteen\");";

        var result = SurgicalPatchEngine.ApplyPatch(original, target, replacement, startLine: 12, endLine: 18);

        Assert.True(result.Success);
        var resultLines = result.PatchedText.Split('\n');
        // Line 5 (index 4) should still have the original
        Assert.Contains("TargetDuplicate", resultLines[4]);
        Assert.DoesNotContain("ReplacementFifteen", resultLines[4]);
        // Line 15 (index 14) should have the replacement
        Assert.Contains("ReplacementFifteen", resultLines[14]);
        Assert.DoesNotContain("TargetDuplicate", resultLines[14]);
    }

    [Fact]
    public void PatchEngine_Fails_ReturnsRichDiagnosticContextWithSurroundingLines()
    {
        var original = "line 1\nline 2\npublic void ProcessOrder(Order order)\n{\n    if (order == null) throw new ArgumentNullException();\n    Save(order);\n}\nline 8\nline 9";
        // Target has a slight discrepancy (typo in order parameter)
        var target = "public void ProcessOrder(Order missingOrder)\n{\n    if (order == null) throw new ArgumentNullException();";
        var replacement = "public void ProcessOrder(Order order) { }";

        var result = SurgicalPatchEngine.ApplyPatch(original, target, replacement);

        Assert.False(result.Success);
        // Rich diagnostic error payload checks
        Assert.Contains("Closest match found near line", result.ErrorMessage);
        Assert.Contains("ProcessOrder", result.ErrorMessage);
        Assert.Contains("Actionable hint:", result.ErrorMessage);
        Assert.Contains("view_file", result.ErrorMessage);
    }
}
