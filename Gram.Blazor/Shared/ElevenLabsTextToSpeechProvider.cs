using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class ElevenLabsTextToSpeechProvider : ITextToSpeechProvider
{
    private const string FallbackVoiceId = "jHprmvvyQreWpRuutdmV";
    private const string FallbackModelId = "eleven_flash_v2_5";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _apiKey;
    private readonly string _voiceId;
    private readonly string _modelId;

    public string ProviderName => "ElevenLabs";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);
    public string DefaultVoiceId => _voiceId;
    public IReadOnlyList<TextToSpeechVoice> Voices { get; } =
    [
        new("tQ4MEZFJOzsahSEEZtHK", "Sana", "Female · warm and seductive"),
        new("jHprmvvyQreWpRuutdmV", "Graham", "Male · default Gram voice"),
        new("v9I7auPeR1xGKYRPwQGG", "Jon", "Male · alternate voice")
    ];

    public ElevenLabsTextToSpeechProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY")
            ?? configuration["ElevenLabs:ApiKey"];
        _voiceId = Environment.GetEnvironmentVariable("ELEVENLABS_VOICE_ID")
            ?? configuration["ElevenLabs:VoiceId"]
            ?? FallbackVoiceId;
        _modelId = Environment.GetEnvironmentVariable("ELEVENLABS_MODEL_ID")
            ?? configuration["ElevenLabs:ModelId"]
            ?? FallbackModelId;
    }

    public async Task<Stream> SynthesizeAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("ElevenLabs is not configured. Set ELEVENLABS_API_KEY.");

        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Text is required.", nameof(request));

        var voiceId = string.IsNullOrWhiteSpace(request.VoiceId) ? _voiceId : request.VoiceId;
        var modelId = string.IsNullOrWhiteSpace(request.ModelId) ? _modelId : request.ModelId;
        var outputFormat = string.IsNullOrWhiteSpace(request.OutputFormat) ? "mp3_22050_32" : request.OutputFormat;

        var endpoint = $"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(voiceId)}/stream?output_format={Uri.EscapeDataString(outputFormat)}";
        var payload = JsonSerializer.Serialize(new
        {
            text = request.Text,
            model_id = modelId,
            voice_settings = new
            {
                stability = 0.45,
                similarity_boost = 0.75,
                style = 0.1,
                use_speaker_boost = true
            }
        });

        var client = _httpClientFactory.CreateClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Headers.TryAddWithoutValidation("xi-api-key", _apiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw new HttpRequestException($"ElevenLabs returned {(int)response.StatusCode}: {error}");
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new ResponseStream(response, responseStream);
    }

    private sealed class ResponseStream : Stream
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _inner;

        public ResponseStream(HttpResponseMessage response, Stream inner)
        {
            _response = response;
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            _response.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
