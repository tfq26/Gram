using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class MarkdownRendererTests
{
    private readonly MarkdownService _service = new();

    [Fact]
    public void PluralParentheticals_AreNotConvertedToMath()
    {
        var input = "Please update the file(s), method(s), and item(s).";
        var html = _service.RenderToHtml(input);

        Assert.Contains("file(s)", html);
        Assert.Contains("method(s)", html);
        Assert.Contains("item(s)", html);
        Assert.DoesNotContain("$s$", html);
        Assert.DoesNotContain("math", html);
    }

    [Fact]
    public void EscapedApostrophesAndContractions_AreCleanlyNormalized()
    {
        // Contractions and possessives with single, double, or triple backslashes
        var input = @"Here is Gram\'s tool, user\\'s setting, and it\\\'s working. We don\'t fail.";
        var html = _service.RenderToHtml(input);

        Assert.Contains("Gram's", html);
        Assert.Contains("user's", html);
        Assert.Contains("it's", html);
        Assert.Contains("don't", html);
        Assert.DoesNotContain(@"\'", html);
        Assert.DoesNotContain(@"\\'", html);
    }

    [Fact]
    public void CodeBlocks_PreserveBackslashesAndEscapes()
    {
        var input = "In C#, use `char c = '\\'';` and in regex use `\\s+`.\n\n```csharp\nvar text = Regex.Replace(text, @\"\\s+\", \" \");\n```";
        var html = _service.RenderToHtml(input);

        Assert.Contains("<code>char c = '\\'';</code>", html);
        Assert.Contains("<code>\\s+</code>", html);
        Assert.Contains("@&quot;\\s+&quot;", html);
    }

    [Fact]
    public void FunctionSignaturesInProse_ArePreservedWithoutMathConversion()
    {
        var input = "ApplyPromoCode_AppliesKnownDiscounts(promoCode: \"SAVE25\", subtotal: 100, expected: 75) — Expected 75, Actual 85.00";
        var html = _service.RenderToHtml(input);

        Assert.Contains("ApplyPromoCode_AppliesKnownDiscounts(promoCode: &quot;SAVE25&quot;, subtotal: 100, expected: 75)", html);
        Assert.DoesNotContain("$$", html);
        Assert.DoesNotContain("math", html);
    }

    [Fact]
    public void RealMathFormulas_AreStillNormalized()
    {
        var input = @"Quadratic discriminant condition is (\Delta < 0) and equation is (a = 2).";
        var html = _service.RenderToHtml(input);

        Assert.Contains("math", html);
        Assert.Contains(@"\Delta &lt; 0", html);
    }

    [Fact]
    public void ListMarkersAndNumberedCitations_AreNotConvertedToMath()
    {
        var input = "See options (a), (b), and citation (1).";
        var html = _service.RenderToHtml(input);

        Assert.Contains("(a)", html);
        Assert.Contains("(b)", html);
        Assert.Contains("(1)", html);
        Assert.DoesNotContain("$a$", html);
        Assert.DoesNotContain("$b$", html);
        Assert.DoesNotContain("$1$", html);
    }

    [Fact]
    public void GitHubAlerts_RenderAlertBlocks()
    {
        var input = "> [!NOTE]\n> This is an important note.\n\n> [!WARNING]\n> Be careful here.";
        var html = _service.RenderToHtml(input);

        Assert.Contains("markdown-alert", html);
        Assert.Contains("markdown-alert-note", html);
        Assert.Contains("markdown-alert-warning", html);
        Assert.Contains("This is an important note.", html);
    }

    [Fact]
    public void CodeBlockWithFilename_RendersFilenameAndLanguage()
    {
        var input = "```csharp:OrderPricingService.cs\nvar x = 1;\n```";
        var html = _service.RenderToHtml(input);

        Assert.Contains("code-filename", html);
        Assert.Contains("OrderPricingService.cs", html);
        Assert.Contains("language-csharp", html);
        Assert.Contains("C#", html);
    }

    [Fact]
    public void MarkdownTable_WrapsInScrollContainerAndCopyToolbar()
    {
        var input = "| Header 1 | Header 2 |\n| --- | --- |\n| Cell 1 | Cell 2 |";
        var html = _service.RenderToHtml(input);

        Assert.Contains("dt-table-wrapper", html);
        Assert.Contains("dt-table-toolbar", html);
        Assert.Contains("btn-copy-table", html);
        Assert.Contains("dt-table-scroll", html);
        Assert.Contains("Cell 1", html);
    }

    [Fact]
    public void FileLinks_AreEnhancedWithFileIconAndClass()
    {
        var input = "[OrderPricingService.cs](file:///Users/user/Projects/OrderPricingService.cs#L10-L20)";
        var html = _service.RenderToHtml(input);

        Assert.Contains("dt-file-link", html);
        Assert.Contains("dt-file-icon", html);
        Assert.Contains("file:///Users/user/Projects/OrderPricingService.cs#L10-L20", html);
    }

    [Fact]
    public void StreamingUnclosedFence_IsStabilizedIntoValidCodeBlock()
    {
        // Unclosed fence in active stream
        var input = "Here is the code:\n```csharp\nint x = 42;";
        var html = _service.RenderToHtml(input);

        Assert.Contains("code-block-wrapper", html);
        Assert.Contains("language-csharp", html);
        Assert.Contains("int x = 42;", html);
    }
}
