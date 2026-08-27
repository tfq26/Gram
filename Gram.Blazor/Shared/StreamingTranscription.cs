namespace Gram.Blazor.Shared;

public sealed record StreamingTranscriptUpdate(string Text, bool IsFinal, string Provider);

public interface IStreamingTranscriber : IAsyncDisposable
{
    event Func<StreamingTranscriptUpdate, Task>? TranscriptReceived;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task SendAudioAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken cancellationToken = default);
    Task CompleteAsync(CancellationToken cancellationToken = default);
}

public interface IStreamingTranscriberFactory
{
    string ProviderName { get; }
    bool IsConfigured { get; }
    IStreamingTranscriber Create();
}
