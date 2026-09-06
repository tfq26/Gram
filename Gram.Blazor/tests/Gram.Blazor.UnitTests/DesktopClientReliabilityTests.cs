using System.Net;
using System.Text;
using Gram.Blazor.Shared;
using Gram.Desktop;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class DesktopClientReliabilityTests
{
    [Fact]
    public async Task SubscribeToRunAsync_SendsLastEventIdHeader_AndStopsAtTerminalEvent()
    {
        string? capturedLastEventId = null;
        var handler = new DelegatingTestHandler(request =>
        {
            if (request.Headers.TryGetValues("Last-Event-ID", out var values))
            {
                capturedLastEventId = values.FirstOrDefault();
            }

            var sse = new StringBuilder();
            sse.AppendLine("data: {\"runId\":\"r1\",\"sequence\":6,\"eventType\":\"text.delta\",\"payload\":\"Hello \"}\n");
            sse.AppendLine("data: {\"runId\":\"r1\",\"sequence\":7,\"eventType\":\"text.delta\",\"payload\":\"world!\"}\n");
            sse.AppendLine("data: {\"runId\":\"r1\",\"sequence\":8,\"eventType\":\"run.completed\",\"payload\":\"done\"}\n");
            sse.AppendLine("data: {\"runId\":\"r1\",\"sequence\":9,\"eventType\":\"text.delta\",\"payload\":\"should not arrive\"}\n");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse.ToString(), Encoding.UTF8, "text/event-stream")
            };
        });

        var client = new GramBackendClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:7337") });
        var received = new List<RunEvent>();

        await foreach (var item in client.SubscribeToRunAsync("r1", afterSequence: 5))
        {
            received.Add(item);
        }

        Assert.Equal("5", capturedLastEventId);
        Assert.Equal(3, received.Count);
        Assert.Equal("run.completed", received[^1].EventType);
        Assert.DoesNotContain(received, r => r.Sequence == 9);
    }

    [Fact]
    public async Task DesktopSession_SequenceDeduplication_PreventsDuplicateTextOnReconnect()
    {
        // Simulate a reconnect scenario where event 1 arrives, then reconnect happens and re-delivers event 1 and then event 2
        var handler = new DelegatingTestHandler(request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/runs"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"run-dup-test\",\"conversationId\":\"conv-1\",\"status\":\"queued\"}", Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("/runs/run-dup-test/events"))
            {
                var sse = new StringBuilder();
                // Event sequence 1 delivered
                sse.AppendLine("data: {\"runId\":\"run-dup-test\",\"sequence\":1,\"eventType\":\"text.delta\",\"payload\":\"Chunk1 \"}\n");
                // Event sequence 1 replayed (duplicate)
                sse.AppendLine("data: {\"runId\":\"run-dup-test\",\"sequence\":1,\"eventType\":\"text.delta\",\"payload\":\"Chunk1 \"}\n");
                // Event sequence 2 delivered
                sse.AppendLine("data: {\"runId\":\"run-dup-test\",\"sequence\":2,\"eventType\":\"text.delta\",\"payload\":\"Chunk2\"}\n");
                // Terminal event
                sse.AppendLine("data: {\"runId\":\"run-dup-test\",\"sequence\":3,\"eventType\":\"run.completed\",\"payload\":\"done\"}\n");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sse.ToString(), Encoding.UTF8, "text/event-stream")
                };
            }

            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("/conversations/conv-1"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"conv-1\",\"title\":\"Test\",\"created\":\"2026-01-01T00:00:00Z\",\"updated\":\"2026-01-01T00:00:00Z\",\"version\":1,\"messages\":[]}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new GramBackendClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:7337") });
        var session = new DesktopSession(client);

        // Pre-set active conversation
        var property = typeof(DesktopSession).GetProperty("ActiveConversation");
        property!.SetValue(session, new ConversationDetail("conv-1", "Test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "default", [], []));

        await session.SendAsync("Hello");

        // "Chunk1 " must appear exactly once, not twice
        Assert.Equal("Chunk1 Chunk2", session.DraftResponse);
    }

    [Fact]
    public async Task DesktopSession_UnauthorizedError_SetsIsUnauthorizedFlag()
    {
        var handler = new DelegatingTestHandler(request =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("Unauthorized access", Encoding.UTF8, "text/plain")
            });

        var client = new GramBackendClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:7337") });
        var session = new DesktopSession(client);

        await session.LoadAsync();

        Assert.True(session.IsUnauthorized);
        Assert.False(session.BackendUnavailable);
        Assert.Contains("Reauthorization", session.Error ?? "");
    }

    [Fact]
    public async Task DesktopSession_ServiceUnavailable_SetsBackendUnavailableFlag()
    {
        var handler = new DelegatingTestHandler(request =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Backend is down for maintenance", Encoding.UTF8, "text/plain")
            });

        var client = new GramBackendClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:7337") });
        var session = new DesktopSession(client);

        await session.LoadAsync();

        Assert.True(session.BackendUnavailable);
        Assert.False(session.IsUnauthorized);
        Assert.Contains("unavailable", session.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DelegatingTestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public DelegatingTestHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
