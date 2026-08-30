using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed record ChatRunUpdate(RunSummary? Run, string? ContentDelta, ToolCallInfo? ToolCall, string? ErrorMessage);
public sealed record ChatRunResult(RunSummary? Run, string Output, string? ErrorMessage, bool Cancelled);

/// Owns the transport and lifecycle of a chat run. UI components receive normalized
/// updates and do not need to know whether the backend is in-process or remote.
public sealed class ChatSessionController
{
    private readonly IRunApi _runs;

    public ChatSessionController(IRunApi runs) => _runs = runs;

    public async Task<ChatRunResult> ExecuteAsync(
        string conversationId,
        SendMessageRequest request,
        Func<ChatRunUpdate, Task> onUpdate,
        CancellationToken cancellationToken = default)
    {
        RunSummary? run = null;
        var output = new System.Text.StringBuilder();
        try
        {
            run = await _runs.StartAsync(conversationId, request, cancellationToken);
            long lastSequence = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var item in _runs.GetEvents(run.Id, lastSequence))
                {
                    lastSequence = item.Sequence;
                    if (item.EventType != "run.step") continue;
                    var step = JsonSerializer.Deserialize<AgentStepUpdate>(item.Payload, BackendJson.Options);
                    if (step is null) continue;
                    if (!string.IsNullOrEmpty(step.ContentDelta)) output.Append(step.ContentDelta);
                    await onUpdate(new ChatRunUpdate(run, step.ContentDelta, step.ToolCall, step.ErrorMessage));
                }

                run = _runs.Get(run.Id) ?? run;
                if (run.CompletedAt is not null) break;
                await Task.Delay(100, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await _runs.CancelAsync(run.Id, "Generation cancelled.");
                return new ChatRunResult(run, output.ToString(), "Generation cancelled.", true);
            }

            return new ChatRunResult(run, output.ToString(), run.Error, run.Status.Equals(nameof(AgentRunStatus.Cancelled), StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException)
        {
            if (run is not null) await _runs.CancelAsync(run.Id, "Generation cancelled.");
            return new ChatRunResult(run, output.ToString(), "Generation cancelled.", true);
        }
    }
}
