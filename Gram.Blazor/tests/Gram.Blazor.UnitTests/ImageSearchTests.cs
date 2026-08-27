using System.Net;
using System.Net.Http.Headers;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ImageSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gram_images_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WikimediaProvider_ReturnsImageAndLicenseMetadata()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"query":{"pages":{"1":{"title":"File:Mountain.jpg","canonicalurl":"https://commons.wikimedia.org/wiki/File:Mountain.jpg","imageinfo":[{"thumburl":"https://upload.wikimedia.org/wikipedia/commons/m/mountain.jpg","extmetadata":{"LicenseShortName":{"value":"CC BY-SA 4.0"},"Artist":{"value":"A Photographer"}}}]}}}}""")
        });
        var provider = new WikimediaImageSearchProvider(new TestFactory(handler), NullLogger<WikimediaImageSearchProvider>.Instance);

        var results = await provider.SearchAsync("mountain", 1);

        var result = Assert.Single(results);
        Assert.Equal("CC BY-SA 4.0", result.License);
        Assert.Equal("A Photographer", result.Creator);
    }

    [Fact]
    public async Task AssetService_CachesImageAndPreservesSourceUrl()
    {
        var handler = new StubHandler(request => request.RequestUri!.Host == "upload.wikimedia.org"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([137, 80, 78, 71]) }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"query":{"pages":{"1":{"title":"File:Mountain.jpg","canonicalurl":"https://commons.wikimedia.org/wiki/File:Mountain.jpg","imageinfo":[{"thumburl":"https://upload.wikimedia.org/wikipedia/commons/m/mountain.jpg","extmetadata":{"LicenseShortName":{"value":"CC BY-SA 4.0"}}}]}}}}""")
            });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDir"] = _root }).Build();
        var storage = new StorageService(configuration);
        var provider = new WikimediaImageSearchProvider(new TestFactory(handler), NullLogger<WikimediaImageSearchProvider>.Instance);
        var service = new PresentationImageAssetService([provider], new TestFactory(handler), storage, NullLogger<PresentationImageAssetService>.Instance);

        var cached = await service.SearchAndCacheAsync("mountains", "project", "section");

        Assert.NotNull(cached);
        Assert.True(File.Exists(cached!.Asset.Path));
        Assert.Equal("https://commons.wikimedia.org/wiki/File:Mountain.jpg", cached.Source.SourcePageUrl);
    }

    private sealed class TestFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(callback(request));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
