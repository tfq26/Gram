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
        GramLog.Info("RunManager", $"Starting run {runId} | Model: {modelId} | Prompt: \"{request.Text}\" | Workspace: {request.WorkspacePath}");
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var router = scope.ServiceProvider.GetRequiredService<IAgentWorkflowRouter>();
            var security = scope.ServiceProvider.GetRequiredService<AgentSecurityService>();

            void OnPermissionReq(PendingPermissionRequest permReq)
            {
                var runScopedReq = permReq with { RunId = runId };
                GramLog.Info("RunManager", $"Permission requested: {permReq.ToolName} | Action: {permReq.Reason}");
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
                            GramLog.Info("RunManager", $"[Tool Result] {existing.ToolName} (Id: {existing.Id}) | Error: {existing.IsError} | Output: {existing.Result}");
                        }
                        else
                        {
                            runToolCalls.Add(tc);
                            GramLog.Info("RunManager", $"[Tool Call] {tc.ToolName} (Id: {tc.Id}) | Args: {tc.Arguments}");
                        }
                    }

                    var statusText = !string.IsNullOrWhiteSpace(step.StepCommentary)
                        ? step.StepCommentary
                        : (step.ToolCall is null ? "Generating response" : $"Using {ToolNameHelper.GetDisplayName(step.ToolCall.ToolName)}");

                    _tracker.Update(runId, AgentRunStatus.Executing, statusText, toolName: step.ToolCall?.ToolName);
                    _tracker.EmitPayload(runId, "run.step", JsonSerializer.Serialize(new { step.ContentDelta, step.ThinkingDelta, step.StepCommentary, step.ToolCall, step.ErrorMessage, step.IsComplete }, BackendJson.Options));
                    if (step.ErrorMessage is not null)
                    {
                        GramLog.Error("RunManager", $"Run {runId} step error: {step.ErrorMessage}");
                        _tracker.Complete(runId, AgentRunStatus.Failed, step.ErrorMessage);
                        return;
                    }
                }
                _tracker.Complete(runId);
                GramLog.Info("RunManager", $"Run {runId} completed successfully.");
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
        catch (OperationCanceledException)
        {
            GramLog.Warn("RunManager", $"Run {runId} cancelled.");
            _tracker.Complete(runId, AgentRunStatus.Cancelled, "Generation cancelled.");
        }
        catch (Exception ex)
        {
            GramLog.Error("RunManager", $"Run {runId} execution failed: {ex.Message}", ex);
            _tracker.Complete(runId, AgentRunStatus.Failed, ex.Message);
        }
    }
}
