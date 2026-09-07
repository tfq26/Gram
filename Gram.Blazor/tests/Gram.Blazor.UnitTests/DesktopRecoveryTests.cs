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
        var agent = new DesktopLocalAgent(new DesktopLocalAgentOptions("http://127.0.0.1:7340", Timeout: TimeSpan.FromMilliseconds(50)), new RejectingHandler());
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
            var localAgent = new DesktopLocalAgent(new DesktopLocalAgentOptions("http://127.0.0.1:7340", Timeout: TimeSpan.FromMilliseconds(50)), new RejectingHandler());
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

    [Fact]
    public void SqliteOfflineCache_PersistsAndQueuesEdits()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gram-cache-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var cache = new DesktopOfflineCache(path);
            Assert.True(cache.IsSqlite);

            var now = DateTimeOffset.UtcNow;
            var summary = new ConversationSummary("c-sqlite-1", "Sqlite Cached", now, now, "gram-q", 1, 0);
            var run = new RunRecoveryState("run-101", "c-sqlite-1", 5, now);
            cache.Write(new DesktopCacheSnapshot([summary], [], [], run));

            var restored = cache.Read();
            Assert.Single(restored.SafeConversations);
            Assert.Equal("c-sqlite-1", restored.SafeConversations[0].Id);
            Assert.Equal("run-101", restored.ActiveRun?.RunId);

            // Enqueue offline edits
            var edit1 = new OfflineQueuedEdit("e1", "c-sqlite-1", "append_message", "{\"role\":\"user\",\"text\":\"hello\"}", now, 1);
            var edit2 = new OfflineQueuedEdit("e2", "c-sqlite-1", "append_message", "{\"role\":\"user\",\"text\":\"world\"}", now.AddSeconds(1), 2);
            var editOther = new OfflineQueuedEdit("e3", "c-other", "append_message", "{\"role\":\"user\",\"text\":\"other\"}", now, 0);

            cache.EnqueueEdit(edit1);
            cache.EnqueueEdit(edit2);
            cache.EnqueueEdit(editOther);

            var conversationEdits = cache.GetQueuedEdits("c-sqlite-1");
            Assert.Equal(2, conversationEdits.Count);
            Assert.Equal("e1", conversationEdits[0].Id);
            Assert.Equal("e2", conversationEdits[1].Id);

            var allEdits = cache.GetQueuedEdits();
            Assert.Equal(3, allEdits.Count);

            // Dequeue / remove edit
            cache.RemoveQueuedEdit("e1");
            var remaining = cache.GetQueuedEdits("c-sqlite-1");
            Assert.Single(remaining);
            Assert.Equal("e2", remaining[0].Id);
        }
        finally
        {
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    [Fact]
    public void VersionConflictException_CapturesStateCorrectly()
    {
        var ex = new VersionConflictException("conv-123", 5, 3);
        Assert.Equal("conv-123", ex.ConversationId);
        Assert.Equal(5, ex.ServerVersion);
        Assert.Equal(3, ex.ExpectedVersion);
        Assert.Contains("Version conflict", ex.Message);
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));
    }
}
