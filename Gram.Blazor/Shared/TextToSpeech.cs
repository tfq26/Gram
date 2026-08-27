namespace Gram.Blazor.Shared;

public sealed record TextToSpeechVoice(string Id, string Name, string Description);

public sealed record TextToSpeechRequest(
    string Text,
    string? VoiceId = null,
    string? ModelId = null,
    string OutputFormat = "mp3_22050_32");

public interface ITextToSpeechProvider
{
    string ProviderName { get; }
    bool IsConfigured { get; }
    string DefaultVoiceId { get; }
    IReadOnlyList<TextToSpeechVoice> Voices { get; }

    Task<Stream> SynthesizeAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken = default);
}
