using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class AssemblyAiStreamingTranscriberFactory : IStreamingTranscriberFactory
{
    private readonly string? _apiKey;
    private readonly Uri _realtimeUri;

    public string ProviderName => "AssemblyAI";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public AssemblyAiStreamingTranscriberFactory(IConfiguration configuration)
    {
        _apiKey = Environment.GetEnvironmentVariable("ASSEMBLYAI_API_KEY")
            ?? configuration["AssemblyAI:ApiKey"];

        var endpoint = Environment.GetEnvironmentVariable("ASSEMBLYAI_REALTIME_URL")
            ?? configuration["AssemblyAI:RealtimeUrl"]
            ?? "wss://streaming.assemblyai.com/v3/ws?sample_rate=16000&speech_model=u3-rt-pro";

        _realtimeUri = new Uri(endpoint, UriKind.Absolute);
    }

    public IStreamingTranscriber Create()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AssemblyAI is not configured. Set ASSEMBLYAI_API_KEY.");

        return new AssemblyAiStreamingTranscriber(_apiKey!, _realtimeUri);
    }
}

public sealed class AssemblyAiStreamingTranscriber : IStreamingTranscriber
{
    private readonly string _apiKey;
    private readonly Uri _realtimeUri;
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _readerTask;
    private bool _completed;

    public event Func<StreamingTranscriptUpdate, Task>? TranscriptReceived;

    public AssemblyAiStreamingTranscriber(string apiKey, Uri realtimeUri)
    {
        _apiKey = apiKey;
        _realtimeUri = realtimeUri;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _socket.Options.SetRequestHeader("Authorization", _apiKey);
        await _socket.ConnectAsync(_realtimeUri, cancellationToken);
        _readerTask = ReadMessagesAsync(_disposeCts.Token);
    }

    public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken cancellationToken = default)
    {
        if (_completed || pcm16Audio.IsEmpty || _socket.State != WebSocketState.Open) return;

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(pcm16Audio, WebSocketMessageType.Binary, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_completed) return;
        _completed = true;

        if (_socket.State == WebSocketState.Open)
        {
            var terminate = Encoding.UTF8.GetBytes("{\"type\":\"Terminate\"}");
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                await _socket.SendAsync(terminate, WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        if (_readerTask != null)
        {
            try { await _readerTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken); }
            catch (TimeoutException) {}
            catch (OperationCanceledException) {}
            catch (WebSocketException) {}
        }
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];
        using var message = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) break;

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
                message.SetLength(0);
                await HandleMessageAsync(json);
            }
        }
        catch (OperationCanceledException) {}
        catch (WebSocketException) {}
    }

    private async Task HandleMessageAsync(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var type = GetString(root, "type") ?? GetString(root, "message_type");

            var isFinal = type switch
            {
                "FinalTranscript" => true,
                "Turn" => GetBoolean(root, "end_of_turn"),
                _ => false
            };

            var text = GetString(root, "transcript") ?? GetString(root, "text");
            if (string.IsNullOrWhiteSpace(text) || type is "Begin" or "SessionBegins") return;

            var handler = TranscriptReceived;
            if (handler != null)
                await handler(new StreamingTranscriptUpdate(text.Trim(), isFinal, "assemblyai"));
        }
        catch (JsonException) {}
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetBoolean(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try { await CompleteAsync(); } catch {}
        }

        _disposeCts.Cancel();
        _socket.Dispose();
        _sendLock.Dispose();
        _disposeCts.Dispose();
    }
}
