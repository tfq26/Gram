using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class GroundedAnswerValidatorTests
{
    private static readonly IReadOnlyList<WebSearchResult> Sources =
    [new WebSearchResult("IRS clean vehicle credits", "https://irs.gov/credits", "The credit ends after 2025.", "Parallel")];

    [Fact]
    public void CitedMaterialClaim_Passes()
    {
        var result = GroundedAnswerValidator.Validate("The credit ends after 2025. [IRS source](https://irs.gov/credits)", Sources);
        Assert.True(result.Passed);
        Assert.Equal(1, result.CitedMaterialClaimCount);
    }

    [Fact]
    public void UnsupportedMaterialClaim_Fails()
    {
        var result = GroundedAnswerValidator.Validate("The credit is $7,500 in 2026.", Sources);
        Assert.False(result.Passed);
        Assert.Contains(result.Issues, issue => issue.Contains("lack a citation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CitationOutsideRetrievedSources_Fails()
    {
        var result = GroundedAnswerValidator.Validate("The rule changed. [Source](https://example.com/rule)", Sources);
        Assert.False(result.Passed);
        Assert.Equal(1, result.InvalidCitationCount);
    }
}
