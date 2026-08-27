using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public interface IToolExecutor
{
    List<AgentToolDefinition> GetAvailableTools(bool isLocalEnvironment, GitAccount? activeGitAccount = null, string? intent = null);
    Task<ToolExecutionResult> ExecuteAsync(string name, string callId, string argumentsJson, string workspacePath, GitAccount? activeGitAccount = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Workflow-facing tool boundary. Policy, sandbox routing, timing, and future
/// idempotency belong here; the registry remains the tool catalog/implementation.
/// </summary>
public sealed class ToolExecutor : IToolExecutor
{
    private readonly IAgentToolRegistry _registry;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(IAgentToolRegistry registry, ILogger<ToolExecutor> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public List<AgentToolDefinition> GetAvailableTools(bool isLocalEnvironment, GitAccount? activeGitAccount = null, string? intent = null) =>
        _registry.GetAvailableTools(isLocalEnvironment, activeGitAccount, intent);

    public async Task<ToolExecutionResult> ExecuteAsync(string name, string callId, string argumentsJson, string workspacePath, GitAccount? activeGitAccount = null, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await _registry.ExecuteToolAsync(name, callId, argumentsJson, workspacePath, activeGitAccount, cancellationToken);
            result.DurationMs = timer.ElapsedMilliseconds;
            _logger.LogInformation("Tool {ToolName} completed with error={IsError} in {DurationMs} ms.", name, result.IsError, result.DurationMs);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} failed before returning a result.", name);
            return new ToolExecutionResult { ToolName = name, CallId = callId, ArgumentsJson = argumentsJson, Output = ex.Message, IsError = true, DurationMs = timer.ElapsedMilliseconds };
        }
    }
}
