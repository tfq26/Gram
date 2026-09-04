namespace Gram.Blazor.Shared;

public sealed record AgentWorkflowRequest(
    string ModelId,
    string UserPrompt,
    List<Message> History,
    string WorkspacePath,
    string? SystemPrompt = null,
    int MaxIterations = 25,
    string? RepositoryMap = null,
    IEnumerable<string>? AllowedToolNames = null,
    string? ForcedWebSearchQuery = null,
    bool IsPresentationMode = false,
    CancellationToken CancellationToken = default,
    bool AllowLocalCapabilities = false);

public interface IAgentWorkflow
{
    string Name { get; }
    bool CanHandle(AgentWorkflowRequest request);
    IAsyncEnumerable<AgentStepUpdate> RunAsync(AgentWorkflowRequest request);
}

public interface IAgentWorkflowRouter
{
    IAsyncEnumerable<AgentStepUpdate> RunAsync(AgentWorkflowRequest request);
}

public sealed class ChatWorkflow : IAgentWorkflow
{
    private readonly AgentOrchestrator _orchestrator;
    public ChatWorkflow(AgentOrchestrator orchestrator) => _orchestrator = orchestrator;
    public string Name => "chat";
    public bool CanHandle(AgentWorkflowRequest request) => !GroundingPolicy.RequiresWebSearch(request.UserPrompt) && string.IsNullOrWhiteSpace(request.ForcedWebSearchQuery);

    public async IAsyncEnumerable<AgentStepUpdate> RunAsync(AgentWorkflowRequest request)
    {
        var iterations = request.AllowLocalCapabilities ? Math.Max(request.MaxIterations, 25) : request.MaxIterations;
        await foreach (var update in _orchestrator.RunAgentLoopAsync(
            request.ModelId, request.History, request.WorkspacePath, request.SystemPrompt,
            iterations, request.RepositoryMap, request.AllowedToolNames,
            cancellationToken: request.CancellationToken, allowLocalCapabilities: request.AllowLocalCapabilities)) yield return update;
    }
}

public sealed class ResearchWorkflow : IAgentWorkflow
{
    private readonly AgentOrchestrator _orchestrator;
    public ResearchWorkflow(AgentOrchestrator orchestrator) => _orchestrator = orchestrator;
    public string Name => "research";
    public bool CanHandle(AgentWorkflowRequest request) => GroundingPolicy.RequiresWebSearch(request.UserPrompt) || !string.IsNullOrWhiteSpace(request.ForcedWebSearchQuery);

    public async IAsyncEnumerable<AgentStepUpdate> RunAsync(AgentWorkflowRequest request)
    {
        var iterations = request.AllowLocalCapabilities ? Math.Max(request.MaxIterations, 25) : request.MaxIterations;
        await foreach (var update in _orchestrator.RunAgentLoopAsync(
            request.ModelId, request.History, request.WorkspacePath, request.SystemPrompt,
            iterations, request.RepositoryMap, request.AllowedToolNames,
            request.ForcedWebSearchQuery ?? request.UserPrompt,
            cancellationToken: request.CancellationToken, allowLocalCapabilities: request.AllowLocalCapabilities)) yield return update;
    }
}

public sealed class PresentationWorkflow : IAgentWorkflow
{
    private static readonly string[] AllowedTools =
    [
        "search_web", "fetch_web_content", "create_presentation_project",
        "get_presentation_status", "get_presentation_logs", "assemble_presentation_html",
        "revise_presentation_section"
    ];
    private readonly AgentOrchestrator _orchestrator;
    public PresentationWorkflow(AgentOrchestrator orchestrator) => _orchestrator = orchestrator;
    public string Name => "presentation";
    public bool CanHandle(AgentWorkflowRequest request) => FeatureFlags.PresentationsEnabled && (request.IsPresentationMode || PresentationGenerationService.IsPresentationRequest(request.UserPrompt));

    public async IAsyncEnumerable<AgentStepUpdate> RunAsync(AgentWorkflowRequest request)
    {
        await foreach (var update in _orchestrator.RunAgentLoopAsync(
            request.ModelId, request.History, request.WorkspacePath, request.SystemPrompt,
            Math.Max(request.MaxIterations, 8), request.RepositoryMap, AllowedTools,
            request.ForcedWebSearchQuery, cancellationToken: request.CancellationToken, allowLocalCapabilities: request.AllowLocalCapabilities)) yield return update;
    }
}

public sealed class CodingWorkflow : IAgentWorkflow
{
    private readonly AgentOrchestrator _orchestrator;
    public CodingWorkflow(AgentOrchestrator orchestrator) => _orchestrator = orchestrator;
    public string Name => "coding";
    public bool CanHandle(AgentWorkflowRequest request) => request.AllowLocalCapabilities && (request.UserPrompt.Contains("code", StringComparison.OrdinalIgnoreCase) || request.UserPrompt.Contains("bug", StringComparison.OrdinalIgnoreCase) || request.UserPrompt.Contains("implement", StringComparison.OrdinalIgnoreCase) || request.UserPrompt.Contains("test", StringComparison.OrdinalIgnoreCase) || request.UserPrompt.Contains("fix", StringComparison.OrdinalIgnoreCase) || request.UserPrompt.Contains("refactor", StringComparison.OrdinalIgnoreCase));
    public async IAsyncEnumerable<AgentStepUpdate> RunAsync(AgentWorkflowRequest request)
    {
        var iterations = Math.Max(request.MaxIterations, 25);
        await foreach (var update in _orchestrator.RunAgentLoopAsync(request.ModelId, request.History, request.WorkspacePath, request.SystemPrompt, iterations, request.RepositoryMap, request.AllowedToolNames, cancellationToken: request.CancellationToken, allowLocalCapabilities: request.AllowLocalCapabilities)) yield return update;
    }
}

public sealed class VoiceWorkflow : IAgentWorkflow
{
    private readonly AgentOrchestrator _orchestrator;
    public VoiceWorkflow(AgentOrchestrator orchestrator) => _orchestrator = orchestrator;
    public string Name => "voice";
    public bool CanHandle(AgentWorkflowRequest request) => request.SystemPrompt?.Contains("voice", StringComparison.OrdinalIgnoreCase) == true;
    public async IAsyncEnumerable<AgentStepUpdate> RunAsync(AgentWorkflowRequest request)
    { await foreach (var update in _orchestrator.RunAgentLoopAsync(request.ModelId, request.History, request.WorkspacePath, request.SystemPrompt, Math.Min(request.MaxIterations, 4), request.RepositoryMap, [], cancellationToken: request.CancellationToken, allowLocalCapabilities: request.AllowLocalCapabilities)) yield return update; }
}

public sealed class AgentWorkflowRouter : IAgentWorkflowRouter
{
    private readonly IReadOnlyList<IAgentWorkflow> _workflows;
    public AgentWorkflowRouter(IEnumerable<IAgentWorkflow> workflows) => _workflows = workflows.ToList();

    public IAsyncEnumerable<AgentStepUpdate> RunAsync(AgentWorkflowRequest request) =>
        (_workflows.FirstOrDefault(workflow => workflow.Name == "presentation" && workflow.CanHandle(request))
         ?? _workflows.FirstOrDefault(workflow => workflow.Name == "research" && workflow.CanHandle(request))
         ?? _workflows.FirstOrDefault(workflow => workflow.Name == "coding" && workflow.CanHandle(request))
         ?? _workflows.FirstOrDefault(workflow => workflow.Name == "voice" && workflow.CanHandle(request))
         ?? _workflows.FirstOrDefault(workflow => workflow.Name == "chat")
         ?? _workflows.First(workflow => workflow.CanHandle(request))).RunAsync(request);
}
