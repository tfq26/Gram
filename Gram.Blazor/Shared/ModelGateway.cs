namespace Gram.Blazor.Shared;

public sealed record ModelRequest(string ModelId, IReadOnlyList<Message> Messages, string? SystemPrompt = null);
public sealed record ModelStreamEvent(string? Text, bool IsComplete = false, string? Error = null);

public interface IModelGateway
{
    IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, CancellationToken cancellationToken = default);
}

public sealed class ModelGateway : IModelGateway
{
    private readonly ModelService _modelService;
    public ModelGateway(ModelService modelService) => _modelService = modelService;

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in _modelService.StreamChatAsync(request.ModelId, request.Messages.ToList(), request.SystemPrompt, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(chunk)) continue;
            if (chunk.StartsWith("[", StringComparison.Ordinal) && chunk.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                yield return new ModelStreamEvent(null, true, chunk);
                yield break;
            }
            yield return new ModelStreamEvent(chunk);
        }
        yield return new ModelStreamEvent(null, true);
    }
}
