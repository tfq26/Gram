using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public enum AgentRunStatus { Queued, Planning, Executing, Waiting, Completed, Failed, Cancelled }

public sealed class AgentRunSnapshot
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string ConversationId { get; init; } = "";
    public string ModelId { get; set; } = "";
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Queued;
    public string StatusText { get; set; } = "Queued";
    public int Iteration { get; set; }
    public int ToolCalls { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
}

public sealed record AgentRunEvent(string RunId, string Type, string Message, DateTimeOffset Timestamp, int Iteration = 0, string? ToolName = null);

public interface IAgentRunTracker
{
    event Action<AgentRunEvent>? OnEvent;
    AgentRunSnapshot Start(string conversationId, string modelId);
    void Update(string runId, AgentRunStatus status, string message, int iteration = 0, string? toolName = null);
    void Complete(string runId, AgentRunStatus status = AgentRunStatus.Completed, string? error = null);
    AgentRunSnapshot? Get(string runId);
    IReadOnlyList<AgentRunEvent> GetEvents(string runId, int max = 20);
}

public sealed class AgentRunTracker : IAgentRunTracker
{
    private readonly StorageService _storage;
    private readonly ILogger<AgentRunTracker> _logger;
    private readonly Dictionary<string, AgentRunSnapshot> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<AgentRunEvent>> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly string _directory;
    public event Action<AgentRunEvent>? OnEvent;

    public AgentRunTracker(StorageService storage, ILogger<AgentRunTracker> logger)
    {
        _storage = storage;
        _logger = logger;
        _directory = _storage.GetFilePath("agent-runs");
        Directory.CreateDirectory(_directory);
    }

    public AgentRunSnapshot Start(string conversationId, string modelId)
    {
        var run = new AgentRunSnapshot { ConversationId = conversationId, ModelId = modelId, Status = AgentRunStatus.Planning, StatusText = "Preparing the request" };
        lock (_sync) { _runs[run.Id] = run; _events[run.Id] = []; Persist(run); }
        Emit(new AgentRunEvent(run.Id, "run.started", run.StatusText, run.StartedAt));
        return run;
    }

    public void Update(string runId, AgentRunStatus status, string message, int iteration = 0, string? toolName = null)
    {
        AgentRunSnapshot? run;
        lock (_sync)
        {
            if (!_runs.TryGetValue(runId, out run)) return;
            run.Status = status; run.StatusText = message; run.Iteration = iteration; run.ToolCalls += toolName is null ? 0 : 1; run.UpdatedAt = DateTimeOffset.UtcNow; Persist(run);
        }
        Emit(new AgentRunEvent(runId, "run.updated", message, DateTimeOffset.UtcNow, iteration, toolName));
    }

    public void Complete(string runId, AgentRunStatus status = AgentRunStatus.Completed, string? error = null)
    {
        lock (_sync)
        {
            if (!_runs.TryGetValue(runId, out var run)) return;
            run.Status = status; run.StatusText = status == AgentRunStatus.Completed ? "Completed" : status.ToString(); run.Error = error; run.CompletedAt = DateTimeOffset.UtcNow; run.UpdatedAt = run.CompletedAt.Value; Persist(run);
        }
        Emit(new AgentRunEvent(runId, "run.completed", error ?? status.ToString(), DateTimeOffset.UtcNow));
    }

    public AgentRunSnapshot? Get(string runId) { lock (_sync) return _runs.TryGetValue(runId, out var run) ? JsonSerializer.Deserialize<AgentRunSnapshot>(JsonSerializer.Serialize(run)) : null; }

    public IReadOnlyList<AgentRunEvent> GetEvents(string runId, int max = 20)
    {
        lock (_sync) return _events.TryGetValue(runId, out var events) ? events.TakeLast(Math.Clamp(max, 1, 100)).ToList() : [];
    }

    private void Persist(AgentRunSnapshot run)
    {
        try { File.WriteAllText(Path.Combine(_directory, $"{run.Id}.json"), JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not persist agent run {RunId}.", run.Id); }
    }

    private void Emit(AgentRunEvent entry)
    {
        lock (_sync)
        {
            if (!_events.TryGetValue(entry.RunId, out var events)) _events[entry.RunId] = events = [];
            events.Add(entry);
            if (events.Count > 100) events.RemoveRange(0, events.Count - 100);
            try { File.AppendAllText(Path.Combine(_directory, $"{entry.RunId}.events.jsonl"), JsonSerializer.Serialize(entry) + Environment.NewLine); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not persist event for agent run {RunId}.", entry.RunId); }
        }
        _logger.LogInformation("Agent run {RunId}: {Type} {Message}", entry.RunId, entry.Type, entry.Message);
        OnEvent?.Invoke(entry);
    }
}
