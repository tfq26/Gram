using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class DiffEngineUnitTests
{
    [Fact]
    public void ComputeDiff_EmptyInputs_ReturnsEmptyDiff()
    {
        var diff = DiffEngine.ComputeDiff("test.cs", "", "");
        Assert.NotNull(diff);
        Assert.Equal("test.cs", diff.FilePath);
        Assert.Empty(diff.Lines);
        Assert.Equal(0, diff.AdditionsCount);
        Assert.Equal(0, diff.DeletionsCount);
    }

    [Fact]
    public void ComputeDiff_AdditionsOnly_CountsCorrectly()
    {
        var oldText = "";
        var newText = "line1\nline2\nline3";

        var diff = DiffEngine.ComputeDiff("file.txt", oldText, newText);

        Assert.Equal(3, diff.AdditionsCount);
        Assert.Equal(0, diff.DeletionsCount);
        Assert.Equal(3, diff.Lines.Count);
        Assert.All(diff.Lines, l => Assert.Equal("add", l.Type));
    }

    [Fact]
    public void ComputeDiff_DeletionsOnly_CountsCorrectly()
    {
        var oldText = "line1\nline2\nline3";
        var newText = "";

        var diff = DiffEngine.ComputeDiff("file.txt", oldText, newText);

        Assert.Equal(0, diff.AdditionsCount);
        Assert.Equal(3, diff.DeletionsCount);
        Assert.Equal(3, diff.Lines.Count);
        Assert.All(diff.Lines, l => Assert.Equal("delete", l.Type));
    }

    [Fact]
    public void ComputeDiff_Modifications_CalculatesBoth()
    {
        var oldText = "public class Example\n{\n    var x = 1;\n}";
        var newText = "public class Example\n{\n    var x = 2;\n    var y = 3;\n}";

        var diff = DiffEngine.ComputeDiff("Example.cs", oldText, newText);

        Assert.True(diff.AdditionsCount >= 2);
        Assert.True(diff.DeletionsCount >= 1);
        Assert.Contains(diff.Lines, l => l.Type == "add" && l.Content.Contains("var x = 2;"));
        Assert.Contains(diff.Lines, l => l.Type == "delete" && l.Content.Contains("var x = 1;"));
        Assert.Contains(diff.Lines, l => l.Type == "context" && l.Content.Contains("public class Example"));
    }

    [Fact]
    public void ParseUnifiedDiff_StandardGitDiff_ParsesProperly()
    {
        var gitDiff = @"diff --git a/Program.cs b/Program.cs
index 1234567..89abcdef 100644
--- a/Program.cs
+++ b/Program.cs
@@ -10,4 +10,5 @@
 Console.WriteLine(""Hello"");
-int a = 1;
+int a = 2;
+int b = 3;
 return;";

        var diff = DiffEngine.ParseUnifiedDiff(gitDiff);

        Assert.NotNull(diff);
        Assert.Equal("Program.cs", diff.FilePath);
        Assert.Equal(2, diff.AdditionsCount);
        Assert.Equal(1, diff.DeletionsCount);
        Assert.Contains(diff.Lines, l => l.Type == "header");
        Assert.Contains(diff.Lines, l => l.Type == "delete" && l.Content == "int a = 1;");
        Assert.Contains(diff.Lines, l => l.Type == "add" && l.Content == "int a = 2;");
        Assert.Contains(diff.Lines, l => l.Type == "add" && l.Content == "int b = 3;");
    }

    [Fact]
    public void ParseUnifiedDiff_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(DiffEngine.ParseUnifiedDiff(""));
        Assert.Null(DiffEngine.ParseUnifiedDiff("   "));
        Assert.Null(DiffEngine.ParseUnifiedDiff(null!));
    }
}
