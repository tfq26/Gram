using System.Net;
using System.Text;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class BackendClientTests
{
    [Fact]
    public async Task GetAsync_PropagatesBearerTokenAndDeserializesContract()
    {
        HttpRequestMessage? request = null;
        using var http = new HttpClient(new RecordingHandler(message =>
        {
            request = message;
            return JsonResponse("{\"id\":\"c1\",\"title\":\"Test\",\"created\":\"2026-01-01T00:00:00Z\",\"updated\":\"2026-01-01T00:00:00Z\",\"modelId\":null,\"messageCount\":0,\"artifactCount\":0,\"version\":1}");
        })) { BaseAddress = new Uri("https://gram.test") };
        var client = new GramBackendClient(http, "access-token");

        var result = await client.GetAsync<ConversationSummary>("/api/v1/conversations");

        Assert.Equal("c1", result.Id);
        Assert.Equal("Bearer access-token", request?.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task SubscribeToRunAsync_ParsesEventsAndSupportsSequenceReplay()
    {
        HttpRequestMessage? request = null;
        using var http = new HttpClient(new RecordingHandler(message =>
        {
            request = message;
            var body = "id: 2\nevent: run.step\ndata: {\"runId\":\"r1\",\"sequence\":2,\"timestamp\":\"2026-01-01T00:00:00Z\",\"eventType\":\"run.step\",\"payload\":\"hello\"}\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
        })) { BaseAddress = new Uri("https://gram.test") };
        var client = new GramBackendClient(http, "access-token");

        var events = new List<RunEvent>();
        await foreach (var item in client.SubscribeToRunAsync("r1", 1)) events.Add(item);

        Assert.Single(events);
        Assert.Equal(2, events[0].Sequence);
        Assert.Equal("1", request?.Headers.GetValues("Last-Event-ID").Single());
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
