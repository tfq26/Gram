using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gram.Blazor.Shared;

public enum GramToolKind
{
    Native,
    Composite,
    Mcp,
    External,
    UserDefined
}

public enum GramToolRisk
{
    Low,
    Medium,
    High,
    Critical
}

public enum GramToolIsolation
{
    InProcess,
    Process,
    GramBox,
    Remote
}

public enum GramToolResultStatus
{
    Completed,
    Failed,
    Denied,
    ApprovalRequired,
    Cancelled
}

public sealed class GramToolManifest
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "General";
    public GramToolKind Kind { get; init; } = GramToolKind.Native;
    public GramToolRisk Risk { get; init; } = GramToolRisk.Low;
    public GramToolIsolation Isolation { get; init; } = GramToolIsolation.InProcess;
    public bool SupportsParallelExecution { get; init; }
    public bool MutatesWorkspace { get; init; }
    public bool MutatesRepository { get; init; }
    public int ExpectedLatencyMs { get; init; } = 100;
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
    public JsonObject InputSchema { get; init; } = new() { ["type"] = "object" };
}

public sealed class GramToolExecutionContext
{
    public string WorkspacePath { get; init; } = Environment.CurrentDirectory;
    public string? RepositoryPath { get; init; }
    public string WorkflowId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public bool ApprovalGranted { get; init; }
    public IReadOnlySet<string> GrantedCapabilities { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "workspace.read",
        "workspace.write",
        "repository.read",
        "repository.write",
        "sandbox.execute",
        "remote.network"
    };
}

public sealed class GramToolExecutionRequest
{
    public string Action { get; init; } = "run";
    public JsonObject Input { get; init; } = new();
    public GramToolExecutionContext Context { get; init; } = new();
    public IReadOnlyDictionary<string, GramToolResult> DependencyResults { get; init; } = new Dictionary<string, GramToolResult>();
    public CancellationToken CancellationToken { get; init; }
}

public sealed class GramToolResult
{
    public string ToolId { get; init; } = "";
    public string Action { get; init; } = "";
    public GramToolResultStatus Status { get; init; }
    public string Output { get; init; } = "";
    public JsonObject Data { get; init; } = new();
    public string? ErrorCode { get; init; }
    public long DurationMs { get; init; }

    public bool IsSuccess => Status == GramToolResultStatus.Completed;

    public static GramToolResult Completed(string toolId, string action, string output, JsonObject? data = null, long durationMs = 0) => new()
    {
        ToolId = toolId,
        Action = action,
        Status = GramToolResultStatus.Completed,
        Output = output,
        Data = data ?? new JsonObject(),
        DurationMs = durationMs
    };

    public static GramToolResult Failed(string toolId, string action, string output, string errorCode = "tool_failed", long durationMs = 0) => new()
    {
        ToolId = toolId,
        Action = action,
        Status = GramToolResultStatus.Failed,
        Output = output,
        ErrorCode = errorCode,
        DurationMs = durationMs
    };

    public static GramToolResult Denied(string toolId, string action, string output, string errorCode = "tool_denied") => new()
    {
        ToolId = toolId,
        Action = action,
        Status = GramToolResultStatus.Denied,
        Output = output,
        ErrorCode = errorCode
    };
}

public sealed class GramToolEvent
{
    public string WorkflowId { get; init; } = "";
    public string RequestId { get; init; } = "";
    public string ToolId { get; init; } = "";
    public string Action { get; init; } = "";
    public string Phase { get; init; } = "";
    public GramToolResult? Result { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public interface IGramTool
{
    GramToolManifest Manifest { get; }
    Task<GramToolResult> ExecuteAsync(GramToolExecutionRequest request);
}

public sealed class GramWorkflowStep
{
    public string Id { get; init; } = "";
    public string ToolId { get; init; } = "";
    public string Action { get; init; } = "run";
    public JsonObject Input { get; init; } = new();
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();
}

public sealed class GramWorkflowDefinition
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public IReadOnlyList<GramWorkflowStep> Steps { get; init; } = Array.Empty<GramWorkflowStep>();
}

public sealed class GramWorkflowResult
{
    public string WorkflowId { get; init; } = "";
    public IReadOnlyDictionary<string, GramToolResult> StepResults { get; init; } = new Dictionary<string, GramToolResult>();
    public bool IsSuccess => StepResults.Count > 0 && StepResults.Values.All(x => x.IsSuccess);
}

public sealed class GramToolPolicyException : Exception
{
    public GramToolPolicyException(string message) : base(message) { }
}

public sealed class GramToolKernel
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IGramTool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly AgentSecurityService _securityService;
    private readonly ToolboxManagerService _toolboxService;

    public event Action<GramToolEvent>? OnEvent;

    public GramToolKernel(
        AgentSecurityService securityService,
        ToolboxManagerService toolboxService,
        IEnumerable<IGramTool> registeredTools)
    {
        _securityService = securityService;
        _toolboxService = toolboxService;

        foreach (var tool in registeredTools)
        {
            Register(tool);
        }

        foreach (var tool in toolboxService.InstalledTools.Where(x => !x.Id.Equals("git-gram", StringComparison.OrdinalIgnoreCase)))
        {
            Register(new ToolboxCatalogTool(tool));
        }
    }

    public IReadOnlyList<GramToolManifest> GetTools() =>
        _tools.Values.Select(x => x.Manifest).OrderBy(x => x.Category).ThenBy(x => x.Name).ToList();

    public void Register(IGramTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (string.IsNullOrWhiteSpace(tool.Manifest.Id))
            throw new ArgumentException("A tool manifest must define an ID.", nameof(tool));

        _tools[tool.Manifest.Id] = tool;
    }

    public async Task<GramToolResult> ExecuteAsync(
        string toolId,
        string action,
        JsonObject? input,
        GramToolExecutionContext context,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, GramToolResult>? dependencyResults = null)
    {
        if (!_tools.TryGetValue(toolId, out var tool))
        {
            return GramToolResult.Failed(toolId, action, $"Tool '{toolId}' is not installed or registered.", "tool_not_found");
        }

        var policy = EvaluatePolicy(tool.Manifest, context);
        if (!policy.IsAllowed)
        {
            var status = policy.RequiresApproval ? GramToolResultStatus.ApprovalRequired : GramToolResultStatus.Denied;
            return new GramToolResult
            {
                ToolId = toolId,
                Action = action,
                Status = status,
                Output = policy.Reason,
                ErrorCode = policy.RequiresApproval ? "approval_required" : "tool_denied"
            };
        }

        var request = new GramToolExecutionRequest
        {
            Action = string.IsNullOrWhiteSpace(action) ? "run" : action,
            Input = input ?? new JsonObject(),
            Context = context,
            DependencyResults = dependencyResults ?? new Dictionary<string, GramToolResult>(),
            CancellationToken = cancellationToken
        };

        OnEvent?.Invoke(new GramToolEvent
        {
            WorkflowId = context.WorkflowId,
            RequestId = context.RequestId,
            ToolId = toolId,
            Action = request.Action,
            Phase = "started"
        });

        var stopwatch = Stopwatch.StartNew();
        SemaphoreSlim? repositoryLock = null;
        try
        {
            if (tool.Manifest.MutatesRepository)
            {
                var repositoryPath = Path.GetFullPath(context.RepositoryPath ?? context.WorkspacePath);
                repositoryLock = RepositoryLocks.GetOrAdd(repositoryPath, _ => new SemaphoreSlim(1, 1));
                await repositoryLock.WaitAsync(cancellationToken);
            }

            var result = await tool.ExecuteAsync(request);
            if (result.DurationMs == 0)
            {
                result = new GramToolResult
                {
                    ToolId = result.ToolId,
                    Action = result.Action,
                    Status = result.Status,
                    Output = result.Output,
                    Data = result.Data,
                    ErrorCode = result.ErrorCode,
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }

            OnEvent?.Invoke(new GramToolEvent
            {
                WorkflowId = context.WorkflowId,
                RequestId = context.RequestId,
                ToolId = toolId,
                Action = request.Action,
                Phase = "completed",
                Result = result
            });
            return result;
        }
        catch (OperationCanceledException)
        {
            var result = new GramToolResult
            {
                ToolId = toolId,
                Action = request.Action,
                Status = GramToolResultStatus.Cancelled,
                Output = "Tool execution was cancelled.",
                ErrorCode = "tool_cancelled",
                DurationMs = stopwatch.ElapsedMilliseconds
            };
            OnEvent?.Invoke(new GramToolEvent { WorkflowId = context.WorkflowId, RequestId = context.RequestId, ToolId = toolId, Action = request.Action, Phase = "cancelled", Result = result });
            return result;
        }
        catch (Exception ex)
        {
            var result = GramToolResult.Failed(toolId, request.Action, ex.Message, "tool_exception", stopwatch.ElapsedMilliseconds);
            OnEvent?.Invoke(new GramToolEvent { WorkflowId = context.WorkflowId, RequestId = context.RequestId, ToolId = toolId, Action = request.Action, Phase = "failed", Result = result });
            return result;
        }
        finally
        {
            if (repositoryLock != null)
            {
                repositoryLock.Release();
            }
        }
    }

    public async Task<GramWorkflowResult> ExecuteWorkflowAsync(
        GramWorkflowDefinition workflow,
        GramToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkflow(workflow);
        var results = new Dictionary<string, GramToolResult>(StringComparer.OrdinalIgnoreCase);
        var remaining = workflow.Steps.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        while (remaining.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ready = remaining.Values
                .Where(step => step.DependsOn.All(results.ContainsKey))
                .ToList();

            if (ready.Count == 0)
                throw new InvalidOperationException("Workflow contains a dependency cycle or an unresolved dependency.");

            var mutating = ready.FirstOrDefault(step => IsRepositoryMutation(step.ToolId));
            var parallelReady = ready.Where(step => CanRunInParallel(step.ToolId)).ToList();
            var batch = mutating != null
                ? new List<GramWorkflowStep> { mutating }
                : parallelReady.Count > 0
                    ? parallelReady
                    : new List<GramWorkflowStep> { ready[0] };

            var executions = batch.Select(async step =>
            {
                var stepContext = new GramToolExecutionContext
                {
                    WorkspacePath = context.WorkspacePath,
                    RepositoryPath = context.RepositoryPath,
                    WorkflowId = workflow.Id,
                    RequestId = $"{workflow.Id}:{step.Id}",
                    ApprovalGranted = context.ApprovalGranted,
                    GrantedCapabilities = context.GrantedCapabilities
                };

                var dependencies = step.DependsOn
                    .Where(results.ContainsKey)
                    .ToDictionary(id => id, id => results[id], StringComparer.OrdinalIgnoreCase);
                var result = await ExecuteAsync(step.ToolId, step.Action, step.Input, stepContext, cancellationToken, dependencies);
                return (step.Id, result);
            });

            foreach (var execution in await Task.WhenAll(executions))
            {
                results[execution.Id] = execution.result;
                remaining.Remove(execution.Id);
            }

            if (batch.Any(step => results[step.Id].Status is GramToolResultStatus.Failed or GramToolResultStatus.Denied or GramToolResultStatus.ApprovalRequired))
                break;
        }

        return new GramWorkflowResult { WorkflowId = workflow.Id, StepResults = results };
    }

    private ToolPolicyDecision EvaluatePolicy(GramToolManifest manifest, GramToolExecutionContext context)
    {
        var missing = manifest.Capabilities.Where(capability => !context.GrantedCapabilities.Contains(capability)).ToList();
        if (missing.Count > 0)
        {
            return new ToolPolicyDecision(false, false, $"Tool '{manifest.Name}' requires capabilities that are not granted: {string.Join(", ", missing)}.");
        }

        if ((manifest.MutatesWorkspace || manifest.MutatesRepository) && _securityService.Settings.PermissionMode == ExecutionPermissionMode.AskAlways && !context.ApprovalGranted)
        {
            return new ToolPolicyDecision(false, true, $"Tool '{manifest.Name}' requires approval before it can modify the workspace or repository.");
        }

        if (manifest.Risk == GramToolRisk.Critical && !context.ApprovalGranted)
        {
            return new ToolPolicyDecision(false, true, $"Tool '{manifest.Name}' is classified as critical-risk and requires approval.");
        }

        return new ToolPolicyDecision(true, false, "Allowed");
    }

    private bool IsRepositoryMutation(string toolId) => _tools.TryGetValue(toolId, out var tool) && tool.Manifest.MutatesRepository;
    private bool CanRunInParallel(string toolId) => _tools.TryGetValue(toolId, out var tool) && tool.Manifest.SupportsParallelExecution && !tool.Manifest.MutatesRepository;

    private void ValidateWorkflow(GramWorkflowDefinition workflow)
    {
        if (workflow.Steps.Count == 0)
            throw new ArgumentException("A workflow must contain at least one step.", nameof(workflow));

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in workflow.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id) || !ids.Add(step.Id))
                throw new ArgumentException($"Workflow contains a duplicate or empty step ID: '{step.Id}'.", nameof(workflow));
            if (!_tools.ContainsKey(step.ToolId))
                throw new ArgumentException($"Workflow references unknown tool '{step.ToolId}'.", nameof(workflow));
        }

        foreach (var dependency in workflow.Steps.SelectMany(x => x.DependsOn))
        {
            if (!ids.Contains(dependency))
                throw new ArgumentException($"Workflow references unknown dependency '{dependency}'.", nameof(workflow));
        }
    }

    private sealed record ToolPolicyDecision(bool IsAllowed, bool RequiresApproval, string Reason);
}

public sealed class GitGramKernelTool : IGramTool
{
    private readonly GitGramService _gitGram;

    public GitGramKernelTool(GitGramService gitGram)
    {
        _gitGram = gitGram;
    }

    public GramToolManifest Manifest { get; } = new()
    {
        Id = "git-gram",
        Name = "Git Gram",
        Description = "Controlled repository operations, status, staging, commits, branches, and audit history.",
        Category = "VCS & Git",
        Kind = GramToolKind.Composite,
        Risk = GramToolRisk.High,
        Isolation = GramToolIsolation.Process,
        SupportsParallelExecution = false,
        MutatesWorkspace = true,
        MutatesRepository = true,
        ExpectedLatencyMs = 250,
        Capabilities = new[] { "workspace.read", "workspace.write", "repository.read", "repository.write" }
    };

    public async Task<GramToolResult> ExecuteAsync(GramToolExecutionRequest request)
    {
        var payload = request.Input["payload"]?.ToString() ?? request.Input["value"]?.ToString() ?? "";
        var result = await _gitGram.ExecuteControlledActionAsync(request.Action, payload, request.Context.WorkspacePath, request.CancellationToken);
        return result.Success
            ? GramToolResult.Completed(Manifest.Id, request.Action, result.Output, result.Data)
            : GramToolResult.Failed(Manifest.Id, request.Action, result.Output, result.ErrorCode ?? "git_gram_failed");
    }
}

public sealed class ToolboxCatalogTool : IGramTool
{
    private readonly ToolboxToolInfo _tool;

    public ToolboxCatalogTool(ToolboxToolInfo tool)
    {
        _tool = tool;
    }

    public GramToolManifest Manifest => new()
    {
        Id = _tool.Id,
        Name = _tool.Name,
        Description = _tool.Description,
        Category = _tool.Category,
        Kind = _tool.Id.StartsWith("mcp", StringComparison.OrdinalIgnoreCase) ? GramToolKind.Mcp : GramToolKind.External,
        Risk = GramToolRisk.Low,
        Isolation = _tool.Id is "embedded-sandbox" or "coder-cde" ? GramToolIsolation.GramBox : GramToolIsolation.Process,
        SupportsParallelExecution = _tool.Id is not "embedded-sandbox" and not "coder-cde",
        MutatesWorkspace = false,
        MutatesRepository = false,
        ExpectedLatencyMs = _tool.Id is "playwright-browser" or "coder-cde" ? 1200 : 100,
        Capabilities = new[] { "workspace.read" }
    };

    public Task<GramToolResult> ExecuteAsync(GramToolExecutionRequest request)
    {
        var payload = request.Input["payload"]?.ToString() ?? "";
        var output = $"[Toolbox: {_tool.Name}]\nAction: {request.Action}\nStatus: {_tool.Status}\nPayload: {payload}\nExecution routed through Gram Tool Kernel.";
        return Task.FromResult(GramToolResult.Completed(Manifest.Id, request.Action, output));
    }
}
