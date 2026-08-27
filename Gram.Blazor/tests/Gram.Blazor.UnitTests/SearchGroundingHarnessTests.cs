using System.Net;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SearchGroundingHarnessTests
{
    [Fact]
    public async Task RunAsync_ReturnsTheSameGroundedContextUsedByTheAgent()
    {
        var provider = new FakeSearchProvider();
        var service = new WebContentService(new TestFactory(), searchProviders: [provider]);
        var harness = new SearchGroundingHarness(service, NullLogger<SearchGroundingHarness>.Instance);

        var run = await harness.RunAsync("What is the latest NASA mission update?", maxResults: 2, bypassCache: true);

        Assert.True(run.IsGrounded);
        Assert.Equal(2, run.Results.Count);
        Assert.Equal(1, run.FetchedPages);
        Assert.Contains("NASA mission update", run.Context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Source-backed detail", run.Context, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeSearchProvider : IWebSearchProvider
    {
        public string Name => "Fixture";
        public bool IsConfigured => true;
        public Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WebSearchResult>>([
                new WebSearchResult("NASA mission update", "https://example.test/nasa", "A current NASA mission update.", Name),
                new WebSearchResult("NASA mission details", "https://example.test/details", "Additional current mission details.", Name)
            ]);
    }

    private sealed class TestFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler());
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><head><title>NASA update</title></head><body><main>Source-backed detail about the NASA mission update.</main></body></html>")
            });
    }
}
