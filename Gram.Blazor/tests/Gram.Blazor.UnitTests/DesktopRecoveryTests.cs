using System.Net;
using System.Text;
using Gram.Blazor.Shared;
using Gram.Desktop;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class DesktopRecoveryTests
{
    [Fact]
    public void OfflineCache_RoundTripsAndRejectsCorruptState()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gram-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new DesktopOfflineCache(path);
            var conversation = new ConversationSummary("c1", "Cached", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "gram-q", 1, 0);
            cache.Write(new DesktopCacheSnapshot([conversation], [], [], new RunRecoveryState("r1", "c1", 4, DateTimeOffset.UtcNow)));

            var restored = cache.Read();
            Assert.Single(restored.SafeConversations);
            Assert.Equal("r1", restored.ActiveRun?.RunId);

            File.WriteAllText(path, "not-json");
            Assert.Empty(cache.Read().SafeConversations);
            Assert.Null(cache.Read().ActiveRun);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LocalAgent_RejectsNonLocalEndpoint()
    {
        Assert.Throws<ArgumentException>(() => new DesktopLocalAgent(new DesktopLocalAgentOptions("https://example.invalid")));
        Assert.Throws<ArgumentException>(() => new DesktopLocalAgent(new DesktopLocalAgentOptions("http://10.0.0.2:7340")));
    }

    [Fact]
    public async Task LocalAgent_ReturnsUnavailableWhenBridgeIsDown()
    {
        var agent = new DesktopLocalAgent(new DesktopLocalAgentOptions("http://127.0.0.1:7340", TimeSpan.FromMilliseconds(50)), new RejectingHandler());
        var status = await agent.GetStatusAsync();
        Assert.False(status.IsAvailable);
    }

    [Fact]
    public async Task DesktopSession_UsesCachedStateWhenBackendIsUnavailable()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gram-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = DateTimeOffset.UtcNow;
            var summary = new ConversationSummary("c1", "Cached", now, now, "gram-q", 0, 0);
            var detail = new ConversationDetail("c1", "Cached", now, now, "gram-q", [], []);
            new DesktopOfflineCache(path).Write(new DesktopCacheSnapshot([summary], [], [detail]));

            var backend = new GramBackendClient(new HttpClient(new RejectingHandler()) { BaseAddress = new Uri("http://localhost:7337") });
            var localAgent = new DesktopLocalAgent(new DesktopLocalAgentOptions("http://127.0.0.1:7340", TimeSpan.FromMilliseconds(50)), new RejectingHandler());
            var session = new DesktopSession(backend, offlineCache: new DesktopOfflineCache(path), localAgent: localAgent);

            await session.LoadAsync();

            Assert.True(session.IsOffline);
            Assert.Single(session.Conversations);
            Assert.False(session.LocalAgentStatus.IsAvailable);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));
    }
}
