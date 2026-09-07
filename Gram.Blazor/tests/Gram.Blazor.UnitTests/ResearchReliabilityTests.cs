using System.Net;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ResearchReliabilityTests
{
    [Fact]
    public void QueryPlanning_AvoidsSpuriousHardwareRumorInjectionForNonTechQueries()
    {
        // Query about economics / inflation
        var plan = SearchPlanBuilder.ForQuestion("What are the latest inflation numbers?");
        Assert.Equal("finance", plan.TopicCategory);
        
        // Ensure NO hardware rumors or release timeline queries are injected
        foreach (var q in plan.Queries)
        {
            Assert.DoesNotContain("hardware roadmap", q.Query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rumors", q.Query, StringComparison.OrdinalIgnoreCase);
        }

        // Tech query SHOULD include hardware roadmap/announcement
        var techPlan = SearchPlanBuilder.ForQuestion("latest nvidia chip announcement");
        Assert.Contains(techPlan.Queries, q => q.Query.Contains("hardware roadmap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GroundingPolicy_WordBoundaryMatchingPreventsSpuriousTopicClassification()
    {
        // "syntax" contains "tax", but should NOT be categorized as regulation
        var decision = GroundingPolicy.Decide("How does syntax work in C#?");
        Assert.NotEqual("regulation", decision.TopicCategory);
    }

    [Fact]
    public void CanonicalUrl_NormalizesTrackingParamsTrailingSlashesAndWww()
    {
        var url1 = "https://www.example.com/article/?utm_source=twitter&utm_medium=social&ref=homepage";
        var url2 = "https://example.com/article";

        var canonical1 = UrlNormalization.CanonicalizeUrl(url1);
        var canonical2 = UrlNormalization.CanonicalizeUrl(url2);

        Assert.Equal("https://example.com/article", canonical1);
        Assert.Equal("https://example.com/article", canonical2);
        Assert.Equal(canonical1, canonical2);
    }

    [Fact]
    public void SourceQualityScorer_FiltersOutDisallowedDomainsWhenPreferredSpecified()
    {
        var plan = new SearchPlan(
            Objective: "SEC 10-K filing",
            Queries: [new SearchQueryPlan("SEC 10-K filing")],
            PreferredDomains: ["sec.gov"]
        );

        var results = new List<WebSearchResult>
        {
            new("SEC Filing", "https://www.sec.gov/edgar/searchedgar/companysearch", "Official SEC filing."),
            new("Blog Review", "https://randomfinanceblog.test/post", "A blog talking about the filing."),
            new("SEC Subdomain", "https://data.sec.gov/api/xbrl/companyfacts", "API feed.")
        };

        var ranked = SourceQualityScorer.Rank(results, plan);

        Assert.Equal(2, ranked.Count);
        Assert.All(ranked, r => Assert.Contains("sec.gov", r.Url, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceQualityScorer_DeduplicatesCanonicalMirrorsAndDuplicateUrls()
    {
        var results = new List<WebSearchResult>
        {
            new("Breaking News: Tech Corp Earnings", "https://reuters.com/tech-corp-earnings?utm_source=rss", "Tech Corp reported earnings today."),
            new("Breaking News: Tech Corp Earnings", "https://reuters.com/tech-corp-earnings", "Tech Corp reported earnings today."),
            new("Breaking News: Tech Corp Earnings", "https://syndicatedmirror.test/tech-corp-earnings", "Tech Corp reported earnings today.")
        };

        var ranked = SourceQualityScorer.Rank(results);

        // Should deduplicate canonical URL and near-duplicate syndicated mirror, leaving only the primary Reuters article
        Assert.Single(ranked);
        Assert.Equal("https://reuters.com/tech-corp-earnings?utm_source=rss", ranked[0].Url);
    }

    [Fact]
    public async Task ScrapeUrlAsync_BlocksRedirectsToUnauthorizedDomains()
    {
        var handler = new RedirectingHandler("https://unauthorized-redirect.test/malicious");
        var clientFactory = new SingleHandlerClientFactory(handler);
        var web = new WebContentService(clientFactory);

        // Scrape with domain restriction allowing only trusted.gov
        var result = await web.ScrapeUrlAsync(
            "https://trusted.gov/report",
            CancellationToken.None,
            allowedDomains: ["trusted.gov"]
        );

        Assert.False(result.IsSuccess);
        Assert.Contains("unauthorized", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResearchEvidenceService_DetectsContradictoryMetricsAcrossSources()
    {
        var sources = new List<ResearchEvidenceSource>
        {
            new("s1", "Report A", "https://sourceA.test", "web", 80, true, "The quarterly revenue reached $50 billion in Q3 with solid margin expansion."),
            new("s2", "Report B", "https://sourceB.test", "web", 80, true, "The quarterly revenue reached $30 billion in Q3 according to restated disclosures.")
        };

        var contradictions = ResearchEvidenceService.DetectContradictions(sources);

        Assert.NotEmpty(contradictions);
        Assert.Equal("s1", contradictions[0].SourceAId);
        Assert.Equal("s2", contradictions[0].SourceBId);
        Assert.Contains("$50 billion", contradictions[0].Description);
        Assert.Contains("$30 billion", contradictions[0].Description);
    }

    [Fact]
    public async Task ScrapeUrlAsync_MultiHopRedirect_BlocksWhenIntermediateDomainUnauthorized()
    {
        var routes = new Dictionary<string, (HttpStatusCode Status, string? Location, string Content)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://trusted.gov/report"] = (HttpStatusCode.Redirect, "https://malicious-tracker.com/hop", ""),
            ["https://malicious-tracker.com/hop"] = (HttpStatusCode.Redirect, "https://trusted.gov/final", ""),
            ["https://trusted.gov/final"] = (HttpStatusCode.OK, null, "<html><body>Final Content</body></html>")
        };

        var handler = new MultiHopMockHandler(routes);
        var clientFactory = new SingleHandlerClientFactory(handler);
        var web = new WebContentService(clientFactory);

        var result = await web.ScrapeUrlAsync(
            "https://trusted.gov/report",
            CancellationToken.None,
            allowedDomains: ["trusted.gov"]
        );

        Assert.False(result.IsSuccess);
        Assert.Contains("unauthorized domain 'malicious-tracker.com'", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://malicious-tracker.com/hop", handler.RequestedUrls);
    }

    [Fact]
    public async Task ScrapeUrlAsync_MultiHopRedirect_BlocksWhenFinalHopUnauthorized()
    {
        var routes = new Dictionary<string, (HttpStatusCode Status, string? Location, string Content)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://source.gov/start"] = (HttpStatusCode.Redirect, "https://redirector.gov/step", ""),
            ["https://redirector.gov/step"] = (HttpStatusCode.Redirect, "https://external-leak.com/data", ""),
            ["https://external-leak.com/data"] = (HttpStatusCode.OK, null, "<html><body>Exfiltrated Content</body></html>")
        };

        var handler = new MultiHopMockHandler(routes);
        var clientFactory = new SingleHandlerClientFactory(handler);
        var web = new WebContentService(clientFactory);

        var result = await web.ScrapeUrlAsync(
            "https://source.gov/start",
            CancellationToken.None,
            allowedDomains: ["source.gov", "redirector.gov"]
        );

        Assert.False(result.IsSuccess);
        Assert.Contains("unauthorized domain 'external-leak.com'", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://source.gov/start", handler.RequestedUrls);
        Assert.Contains("https://redirector.gov/step", handler.RequestedUrls);
        Assert.DoesNotContain("https://external-leak.com/data", handler.RequestedUrls);
    }

    [Fact]
    public async Task ScrapeUrlAsync_DetectsAndBlocksRedirectLoop()
    {
        var routes = new Dictionary<string, (HttpStatusCode Status, string? Location, string Content)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://trusted.gov/loop-a"] = (HttpStatusCode.Redirect, "https://trusted.gov/loop-b", ""),
            ["https://trusted.gov/loop-b"] = (HttpStatusCode.Redirect, "https://trusted.gov/loop-a", "")
        };

        var handler = new MultiHopMockHandler(routes);
        var clientFactory = new SingleHandlerClientFactory(handler);
        var web = new WebContentService(clientFactory);

        var result = await web.ScrapeUrlAsync("https://trusted.gov/loop-a", CancellationToken.None, allowedDomains: ["trusted.gov"]);

        Assert.False(result.IsSuccess);
        Assert.Contains("redirect loop", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScrapeUrlAsync_BlocksHttpsToHttpRedirectDowngrade()
    {
        var routes = new Dictionary<string, (HttpStatusCode Status, string? Location, string Content)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://trusted.gov/secure"] = (HttpStatusCode.Redirect, "http://trusted.gov/insecure", ""),
            ["http://trusted.gov/insecure"] = (HttpStatusCode.OK, null, "<html>Content</html>")
        };

        var handler = new MultiHopMockHandler(routes);
        var clientFactory = new SingleHandlerClientFactory(handler);
        var web = new WebContentService(clientFactory);

        var result = await web.ScrapeUrlAsync("https://trusted.gov/secure", CancellationToken.None, allowedDomains: ["trusted.gov"]);

        Assert.False(result.IsSuccess);
        Assert.Contains("downgrade", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://trusted.gov/insecure", handler.RequestedUrls);
    }

    [Fact]
    public async Task ScrapeUrlAsync_BlocksRedirectToPrivateOrLoopbackIp()
    {
        var routes = new Dictionary<string, (HttpStatusCode Status, string? Location, string Content)>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://trusted.gov/redir-meta"] = (HttpStatusCode.Redirect, "https://169.254.169.254/latest/meta-data", ""),
            ["https://trusted.gov/redir-loop"] = (HttpStatusCode.Redirect, "https://127.0.0.1:8080/internal", "")
        };

        var handler = new MultiHopMockHandler(routes);
        var clientFactory = new SingleHandlerClientFactory(handler);
        var web = new WebContentService(clientFactory);

        var resultMeta = await web.ScrapeUrlAsync("https://trusted.gov/redir-meta", CancellationToken.None);
        Assert.False(resultMeta.IsSuccess);
        Assert.Contains("private, loopback, or reserved", resultMeta.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);

        var resultLoop = await web.ScrapeUrlAsync("https://trusted.gov/redir-loop", CancellationToken.None);
        Assert.False(resultLoop.IsSuccess);
        Assert.Contains("private, loopback, or reserved", resultLoop.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MultiHopMockHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string? Location, string Content)> _routes;
        public List<string> RequestedUrls { get; } = [];

        public MultiHopMockHandler(Dictionary<string, (HttpStatusCode Status, string? Location, string Content)> routes)
        {
            _routes = routes;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            RequestedUrls.Add(url);

            if (_routes.TryGetValue(url, out var route))
            {
                var response = new HttpResponseMessage(route.Status)
                {
                    Content = new StringContent(route.Content),
                    RequestMessage = request
                };
                if (!string.IsNullOrEmpty(route.Location))
                {
                    response.Headers.Location = new Uri(route.Location, UriKind.RelativeOrAbsolute);
                }
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }
    }

    private sealed class SingleHandlerClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleHandlerClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private sealed class RedirectingHandler : HttpMessageHandler
    {
        private readonly string _redirectTarget;
        public RedirectingHandler(string redirectTarget) => _redirectTarget = redirectTarget;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Simulate 302 redirect by returning response with redirected RequestMessage.RequestUri
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>Redirected content</body></html>"),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, _redirectTarget)
            };
            return Task.FromResult(response);
        }
    }
}
