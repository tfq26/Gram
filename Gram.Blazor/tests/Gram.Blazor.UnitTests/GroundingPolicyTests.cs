using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class GroundingPolicyTests
{
    [Fact]
    public void CurrentQuestion_RequiresLiveWebWithoutForcingLegalDomains()
    {
        var decision = GroundingPolicy.Decide("What is the latest NASA mission update?");

        Assert.Equal(GroundingRequirement.LiveWeb, decision.Requirement);
        Assert.False(decision.RequiresPrimarySources);
        Assert.Equal("fast", decision.Mode);
    }

    [Fact]
    public void RegulatoryComparison_RequiresPrimarySourcesAndMultipleQueries()
    {
        var decision = GroundingPolicy.Decide("Compare the current U.S. and EU EV tax and tariff regulations.");

        Assert.True(decision.RequiresPrimarySources);
        Assert.Equal(3, decision.QueryCount);
        Assert.Contains("irs.gov", decision.PreferredDomains!);
        Assert.Contains("eur-lex.europa.eu", decision.PreferredDomains!);
        Assert.DoesNotContain("nasa.gov", decision.PreferredDomains!);
        Assert.Equal("regulation", decision.TopicCategory);
    }
}
