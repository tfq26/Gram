using System.Net;
using System.Text;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class WebSearchDiagnosticsTests
{
    public static IEnumerable<object[]> CurrentInformationPrompts()
    {
        yield return ["What is the plot of Avengers: Doomsday?", "Doomsday"];
        yield return ["What is the latest major NASA mission update?", "NASA"];
        yield return ["What was the latest Federal Reserve interest-rate decision?", "Fed"];
        yield return ["What major artificial intelligence model was announced most recently?", "AI"];
        yield return ["What are the latest Formula 1 championship standings?", "Formula"];
    }

    [Fact]
    public async Task ParallelProvider_UsesSystemConfiguration_FastMode_AndReturnsGroundingResults()
    {
        var handler = new ParallelFixtureHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PARALLEL_API_KEY"] = "parallel-system-test-key"
            })
            .Build();
        var provider = new ParallelWebSearchProvider(new TestHttpClientFactory(handler), configuration);
        var service = new WebContentService(
            new TestHttpClientFactory(handler),
            searchProviders: [provider]);

        var results = await service.SearchWebAsync(
            "What is the plot of Avengers: Doomsday?",
            maxResults: 2,
            bypassCache: true);

        Assert.True(provider.IsConfigured);
        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Equal("Parallel", result.Source));
        Assert.Contains(results, result => result.Snippet.Contains("Doctor Doom", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("parallel-system-test-key", handler.ApiKey);
        Assert.Contains("\"mode\":\"fast\"", handler.RequestBody);
        Assert.Contains("\"objective\":\"What is the plot of Avengers: Doomsday?\"", handler.RequestBody);
        Assert.Contains("\"search_queries\":[\"Avengers: Doomsday\"]", handler.RequestBody);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains(service.ProviderStatuses, status => status.Name == "Parallel" && status.IsConfigured);
    }

    [Fact]
    public async Task ParallelProvider_SendsHardPrimarySourceAllowList()
    {
        var handler = new ParallelFixtureHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PARALLEL_API_KEY"] = "parallel-system-test-key" })
            .Build();
        var provider = new ParallelWebSearchProvider(new TestHttpClientFactory(handler), configuration);
        var plan = new SearchPlan(
            "Find the current official U.S. EV tax-credit rules.",
            [new SearchQueryPlan("U.S. EV tax credit current rules")],
            "advanced",
            "current",
            ["irs.gov", "treasury.gov"],
            true,
            "gemini-3-flash-preview",
            "test-primary-sources");

        await provider.SearchAsync(plan, 3);

        Assert.Contains("\"advanced_settings\"", handler.RequestBody);
        Assert.Contains("\"include_domains\":[\"irs.gov\",\"treasury.gov\"]", handler.RequestBody);
        Assert.Contains("\"client_model\":\"gemini-3-flash-preview\"", handler.RequestBody);
        Assert.Contains("\"session_id\":\"test-primary-sources\"", handler.RequestBody);
    }

    [Fact]
    public async Task ParallelProvider_WithoutSystemKey_DoesNotSendRequest()
    {
        var handler = new ParallelFixtureHandler();
        var provider = new ParallelWebSearchProvider(
            new TestHttpClientFactory(handler),
            new ConfigurationBuilder().Build());

        var results = await provider.SearchAsync("current news", 5);

        Assert.False(provider.IsConfigured);
        Assert.Empty(results);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task TavilyProvider_UsesSystemConfiguration_AndReturnsGroundingResults()
    {
        var handler = new TavilyFixtureHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TAVILY_API_KEY"] = "system-test-key"
            })
            .Build();
        var provider = new TavilyWebSearchProvider(new TestHttpClientFactory(handler), configuration);
        var service = new WebContentService(
            new TestHttpClientFactory(handler),
            searchProviders: [provider]);

        var results = await service.SearchWebAsync(
            "What is the plot of Avengers: Doomsday?",
            maxResults: 2,
            bypassCache: true);

        Assert.True(provider.IsConfigured);
        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Equal("Tavily", result.Source));
        Assert.Contains(results, result => result.Title.Contains("Doomsday", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("system-test-key", handler.AuthorizationParameter);
        Assert.Contains("\"search_depth\":\"basic\"", handler.RequestBody);
        Assert.Contains("\"max_results\":2", handler.RequestBody);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains(service.ProviderStatuses, status => status.Name == "Tavily" && status.IsConfigured);
    }

    [Fact]
    public async Task TavilyProvider_WithoutSystemKey_DoesNotSendRequest()
    {
        var handler = new TavilyFixtureHandler();
        var configuration = new ConfigurationBuilder().Build();
        var provider = new TavilyWebSearchProvider(new TestHttpClientFactory(handler), configuration);

        var results = await provider.SearchAsync("current news", 5);

        Assert.False(provider.IsConfigured);
        Assert.Empty(results);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SearchWebAsync_NormalizesMovieQuestion_AndPrioritizesWikipediaGrounding()
    {
        var handler = new SearchFixtureHandler();
        var service = new WebContentService(new TestHttpClientFactory(handler));

        var results = await service.SearchWebAsync(
            "What is the plot of Avengers: Doomsday?",
            maxResults: 5,
            bypassCache: true);

        Assert.Equal(5, results.Count);
        Assert.Equal("Wikipedia", results[0].Source);
        Assert.Equal("Avengers: Doomsday", results[0].Title);
        Assert.Contains("search=Avengers: Doomsday", Uri.UnescapeDataString(handler.WikipediaRequestUrl));
        Assert.Equal(4, results.Count(result => result.Source?.StartsWith("Google News", StringComparison.Ordinal) == true));
        Assert.True(results.Count(result => !string.IsNullOrWhiteSpace(result.Snippet)) >= 4);
    }

    [Fact]
    [Trait("Category", "LiveSearch")]
    public async Task LiveSearch_ReturnsGrounding_ForFiveCurrentInformationPrompts()
    {
        // External search checks are opt-in so the normal unit-test suite remains deterministic.
        if (!string.Equals(Environment.GetEnvironmentVariable("GRAM_RUN_LIVE_SEARCH_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var service = new WebContentService(
            new TestHttpClientFactory(),
            loggerFactory.CreateLogger<WebContentService>());
        foreach (var row in CurrentInformationPrompts())
        {
            var prompt = Assert.IsType<string>(row[0]);
            var expectedTerm = Assert.IsType<string>(row[1]);
            var results = await service.SearchWebAsync(prompt, maxResults: 5, bypassCache: true);

            Assert.True(results.Count > 0, $"No live search results were returned for: {prompt}");
            Assert.Contains(results, result => !string.IsNullOrWhiteSpace(result.Snippet));
            Assert.Contains(results, result =>
                result.Title.Contains(expectedTerm, StringComparison.OrdinalIgnoreCase)
                || result.Snippet.Contains(expectedTerm, StringComparison.OrdinalIgnoreCase));

            if (expectedTerm.Equals("Doomsday", StringComparison.Ordinal))
            {
                Assert.Equal("Wikipedia", results[0].Source);
                var page = await service.ScrapeUrlAsync(results[0].Url);
                Assert.True(page.IsSuccess, $"The primary Avengers source could not be fetched: {page.ErrorMessage}");
                Assert.Contains("Avengers", page.ContentMarkdown, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Doomsday", page.ContentMarkdown, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    [Trait("Category", "LiveParallelSearch")]
    public async Task LiveParallelSearch_ReturnsCurrentGrounding()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GRAM_RUN_LIVE_PARALLEL_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var provider = new ParallelWebSearchProvider(
            new TestHttpClientFactory(),
            configuration,
            loggerFactory.CreateLogger<ParallelWebSearchProvider>());

        Assert.True(provider.IsConfigured, "PARALLEL_API_KEY was not available to the live test process.");
        var results = await provider.SearchAsync("What is the plot of Avengers: Doomsday?", 5);

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.Equal("Parallel", result.Source));
        Assert.Contains(results, result =>
            result.Title.Contains("Doomsday", StringComparison.OrdinalIgnoreCase)
            || result.Snippet.Contains("Doomsday", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, result => !string.IsNullOrWhiteSpace(result.Snippet));
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler? handler = null) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return handler is null
                ? new HttpClient(new HttpClientHandler
                {
                    UseCookies = false,
                    AutomaticDecompression = DecompressionMethods.All
                })
                : new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class SearchFixtureHandler : HttpMessageHandler
    {
        public string WikipediaRequestUrl { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (url.Contains("api.duckduckgo.com", StringComparison.Ordinal))
            {
                return Json("{}");
            }

            if (url.Contains("html.duckduckgo.com", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
            }

            if (url.Contains("news.google.com", StringComparison.Ordinal))
            {
                const string rss = """
                    <?xml version="1.0" encoding="UTF-8"?>
                    <rss version="2.0"><channel>
                      <item><title>Avengers: Doomsday official synopsis revealed</title><link>https://news.google.com/articles/1</link><description>Marvel shares new Avengers Doomsday story details.</description><source>Example News</source><pubDate>Tue, 25 Aug 2026 12:00:00 GMT</pubDate></item>
                      <item><title>Avengers: Doomsday release update</title><link>https://news.google.com/articles/2</link><description>The upcoming film receives a production update.</description><source>Movie News</source></item>
                      <item><title>Doctor Doom leads the next Avengers film</title><link>https://news.google.com/articles/3</link><description>New reporting covers the movie premise.</description><source>Entertainment Daily</source></item>
                      <item><title>Marvel previews its next crossover</title><link>https://news.google.com/articles/4</link><description>Avengers Doomsday brings several teams together.</description><source>Film Wire</source></item>
                      <item><title>This fifth item should be held back</title><link>https://news.google.com/articles/5</link><description>Reserved slot test.</description><source>Overflow</source></item>
                    </channel></rss>
                    """;
                return Xml(rss);
            }

            if (url.Contains("en.wikipedia.org", StringComparison.Ordinal))
            {
                WikipediaRequestUrl = url;
                return Json("""["Avengers: Doomsday",["Avengers: Doomsday"],[""],["https://en.wikipedia.org/wiki/Avengers:_Doomsday"]]""");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string body)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static Task<HttpResponseMessage> Xml(string body)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/rss+xml")
            });
        }
    }

    private sealed class TavilyFixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);

            const string response = """
                {
                  "query": "What is the plot of Avengers: Doomsday?",
                  "results": [
                    {
                      "title": "Avengers: Doomsday - Official movie information",
                      "url": "https://example.com/avengers-doomsday",
                      "content": "Avengers: Doomsday is an upcoming Marvel Studios film featuring Doctor Doom.",
                      "score": 0.97
                    },
                    {
                      "title": "Avengers: Doomsday story details",
                      "url": "https://example.org/doomsday-story",
                      "content": "Current reporting distinguishes the official premise from unconfirmed plot rumors.",
                      "score": 0.91
                    }
                  ],
                  "response_time": "0.42",
                  "usage": { "credits": 1 }
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ParallelFixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? ApiKey { get; private set; }
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            ApiKey = request.Headers.TryGetValues("x-api-key", out var values) ? values.SingleOrDefault() : null;
            RequestBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);

            const string response = """
                {
                  "search_id": "search_test_123",
                  "results": [
                    {
                      "url": "https://example.com/avengers-doomsday",
                      "title": "Avengers: Doomsday official premise",
                      "publish_date": "2026-08-20",
                      "excerpts": ["Marvel's upcoming film centers on Doctor Doom and a new Avengers conflict."]
                    },
                    {
                      "url": "https://example.org/doomsday-reporting",
                      "title": "What is confirmed about Avengers: Doomsday",
                      "publish_date": null,
                      "excerpts": ["Current reporting separates confirmed story information from plot rumors."]
                    }
                  ],
                  "session_id": "session_test_123",
                  "warnings": null,
                  "usage": [{ "name": "sku_search", "count": 1 }]
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
