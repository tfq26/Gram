using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Gram.Blazor.Shared;

/// Single owner of run execution. Clients observe runs; they do not coordinate
/// provider calls, workflow loops, cancellation, or final persistence.
public sealed class RunManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentRunTracker _tracker;

    public RunManager(IServiceScopeFactory scopeFactory, IAgentRunTracker tracker)
    { _scopeFactory = scopeFactory; _tracker = tracker; }

    public AgentRunSnapshot Start(Conversation conversation, string modelId, SendMessageRequest request)
    {
        var run = _tracker.Start(conversation.Id, modelId);
        _ = ExecuteAsync(run.Id, modelId, conversation, request);
        return run;
    }

    private async Task ExecuteAsync(string runId, string modelId, Conversation conversation, SendMessageRequest request)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var router = scope.ServiceProvider.GetRequiredService<IAgentWorkflowRouter>();
            var security = scope.ServiceProvider.GetRequiredService<AgentSecurityService>();

            void OnPermissionReq(PendingPermissionRequest permReq)
            {
                var runScopedReq = permReq with { RunId = runId };
                _tracker.Update(runId, AgentRunStatus.Executing, $"Approval required: {permReq.ToolName}", toolName: permReq.ToolName);
                _tracker.EmitPayload(runId, "permission.requested", JsonSerializer.Serialize(runScopedReq, BackendJson.Options));
            }
            security.OnPermissionRequested += OnPermissionReq;

            var runToolCalls = new List<ToolCallInfo>();

            try
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_tracker.GetCancellationToken(runId));
                var workspace = string.IsNullOrWhiteSpace(request.WorkspacePath) ? Environment.CurrentDirectory : request.WorkspacePath!;
                var history = request.ContextMessages?.ToList() ?? conversation.Messages;
                await foreach (var step in router.RunAsync(new AgentWorkflowRequest(modelId, request.Text, history, workspace, request.SystemPrompt, RepositoryMap: request.RepositoryMap, AllowedToolNames: request.AllowedToolNames, IsPresentationMode: request.PresentationMode, ForcedWebSearchQuery: request.ForcedSearchQuery, CancellationToken: cancellation.Token, AllowLocalCapabilities: request.AllowLocalCapabilities)))
                {
                    if (step.ContentDelta is { Length: > 0 } delta) _tracker.AppendOutput(runId, delta);

                    if (step.ToolCall is { } tc)
                    {
                        var existing = runToolCalls.FirstOrDefault(t => t.Id == tc.Id);
                        if (existing != null)
                        {
                            existing.Result = tc.Result;
                            existing.IsError = tc.IsError;
                        }
                        else
                        {
                            runToolCalls.Add(tc);
                        }
                    }

                    _tracker.Update(runId, AgentRunStatus.Executing, step.ToolCall is null ? "Generating response" : $"Using {ToolNameHelper.GetDisplayName(step.ToolCall.ToolName)}", toolName: step.ToolCall?.ToolName);
                    _tracker.EmitPayload(runId, "run.step", JsonSerializer.Serialize(new { step.ContentDelta, step.ThinkingDelta, step.ToolCall, step.ErrorMessage, step.IsComplete }, BackendJson.Options));
                    if (step.ErrorMessage is not null) { _tracker.Complete(runId, AgentRunStatus.Failed, step.ErrorMessage); return; }
                }
                _tracker.Complete(runId);
                using var saveScope = _scopeFactory.CreateScope();
                var service = saveScope.ServiceProvider.GetRequiredService<ConversationService>();
                await service.InitializeAsync();
                var saved = service.Conversations.FirstOrDefault(x => x.Id == conversation.Id);
                if (saved is not null)
                {
                    saved.Messages.Add(new Message("assistant", _tracker.GetOutput(runId) ?? "")
                    {
                        ToolCalls = runToolCalls.Count > 0 ? runToolCalls : null
                    });
                    await service.SaveConversationAsync(saved);
                }
            }
            finally
            {
                security.OnPermissionRequested -= OnPermissionReq;
            }

            // Extract only high-signal user-authored patterns. Explicit memory
            // requests become active; inferred facts remain reviewable candidates.
            try
            {
                using var memScope = _scopeFactory.CreateScope();
                var extractor = memScope.ServiceProvider.GetRequiredService<MemoryExtractionService>();
                await extractor.ExtractAsync(request.Text, conversation.Id, conversation.ProjectId);
            }
            catch { /* Memory capture must never fail an otherwise successful run. */ }
        }
        catch (OperationCanceledException) { _tracker.Complete(runId, AgentRunStatus.Cancelled, "Generation cancelled."); }
        catch (Exception ex) { _tracker.Complete(runId, AgentRunStatus.Failed, ex.Message); }
    }
}
