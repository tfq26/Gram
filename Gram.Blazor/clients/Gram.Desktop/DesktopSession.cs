using Gram.Blazor.Shared;
using System.Text.Json;

namespace Gram.Desktop;

/// Frontend-neutral desktop state coordinator. A native UI binds to this class;
/// it should not call HTTP endpoints or provider APIs directly.
public sealed class DesktopSession
{
    private readonly GramBackendClient _backend;
    private CancellationTokenSource? _runCancellation;
    private CancellationTokenSource? _selectConversationCts;

    public DesktopSession(GramBackendClient backend, string? backendUrl = null)
    {
        _backend = backend;
        BackendUrl = backendUrl ?? "http://localhost:7337";
        WorkspacePath = Environment.CurrentDirectory;
    }

    public string BackendUrl { get; }
    public string WorkspacePath { get; private set; }
    public bool LocalCapabilitiesEnabled => Uri.TryCreate(BackendUrl, UriKind.Absolute, out var uri)
        && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ConversationSummary> Conversations { get; private set; } = [];
    public IReadOnlyList<ModelSummary> Models { get; private set; } = [];
    public ConversationDetail? ActiveConversation { get; private set; }
    public RunSummary? ActiveRun { get; private set; }
    public IReadOnlyList<StructuredMemory> RelevantMemories { get; private set; } = [];
    public IReadOnlyList<StructuredMemory> MemoryCandidates { get; private set; } = [];
    public PendingPermissionRequest? ActivePermissionRequest { get; private set; }
    public List<ToolCallInfo> CurrentStreamingToolCalls { get; private set; } = [];
    public ExecutionPermissionMode CurrentPermissionMode { get; private set; } = ExecutionPermissionMode.SafeAutoApprove;
    public bool IsLoading { get; private set; }
    public bool IsStreaming { get; private set; }
    public string DraftResponse { get; private set; } = "";
    public string? Error { get; private set; }

    public event Action? StateChanged;
    public event Action<RunEvent>? RunEventReceived;

    public void SetWorkspace(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Workspace not found: {fullPath}");
        WorkspacePath = fullPath;
        Notify();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        Error = null;
        Notify();
        try
        {
            var conversationsTask = _backend.GetAsync<IReadOnlyList<ConversationSummary>>("/api/v1/conversations", cancellationToken);
            var modelsTask = _backend.GetAsync<IReadOnlyList<ModelSummary>>("/api/v1/models", cancellationToken);
            var securityTask = LoadSecuritySettingsAsync();
            await Task.WhenAll(conversationsTask, modelsTask, securityTask);
            Conversations = await conversationsTask;
            Models = await modelsTask;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
            Notify();
        }
    }

    public async Task LoadSecuritySettingsAsync()
    {
        try
        {
            var settings = await _backend.GetAsync<SecurityGuardSettings>("/api/v1/security/settings");
            if (settings != null)
            {
                CurrentPermissionMode = settings.PermissionMode;
                Notify();
            }
        }
        catch {}
    }

    public async Task SetPermissionModeAsync(ExecutionPermissionMode mode)
    {
        CurrentPermissionMode = mode;
        Notify();
        try
        {
            await _backend.PostAsync<SecurityGuardSettings>("/api/v1/security/settings", new SecurityGuardSettings { PermissionMode = mode });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update permission mode: {ex.Message}");
        }
    }

    public async Task RespondToPermissionAsync(string permissionId, bool approved, bool rememberForSession = false)
    {
        try
        {
            await _backend.PostAsync<object>(
                $"/api/v1/permissions/{Uri.EscapeDataString(permissionId)}/respond",
                new PermissionResponseRequest(approved, rememberForSession));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error responding to permission: {ex.Message}");
        }
        finally
        {
            ActivePermissionRequest = null;
            Notify();
        }
    }

    public async Task SelectConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return;

        // Cancel previous pending selection so older loads don't finish later and clobber active conversation
        _selectConversationCts?.Cancel();
        _selectConversationCts?.Dispose();
        _selectConversationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _selectConversationCts.Token;

        Error = null;
        IsLoading = true;
        Notify();

        try
        {
            // Fetch authoritative latest version of conversation from backend API
            var latest = await _backend.GetAsync<ConversationDetail>(
                $"/api/v1/conversations/{Uri.EscapeDataString(conversationId)}",
                ct);

            ct.ThrowIfCancellationRequested();

            ActiveConversation = latest;
            DraftResponse = "";

            // Synchronize Conversations summary list with latest title and version
            if (Conversations.Any(c => c.Id == conversationId))
            {
                Conversations = Conversations.Select(c => c.Id == conversationId
                    ? c with { Title = latest.Title, Updated = latest.Updated, Version = latest.Version, MessageCount = latest.Messages.Count }
                    : c).ToList();
            }

            await LoadMemoryAsync(ActiveConversation.Messages.LastOrDefault(m => m.Role == "user")?.Text, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Safely ignored: a newer selection request was initiated
            return;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
                Notify();
            }
        }
    }

    public async Task RefreshActiveConversationAsync(CancellationToken cancellationToken = default)
    {
        if (ActiveConversation == null) return;
        try
        {
            var fresh = await _backend.GetAsync<ConversationDetail>(
                $"/api/v1/conversations/{Uri.EscapeDataString(ActiveConversation.Id)}",
                cancellationToken);
            if (fresh != null && fresh.Version >= ActiveConversation.Version)
            {
                ActiveConversation = fresh;
                Notify();
            }
        }
        catch {}
    }

    public async Task CreateConversationAsync(string? modelId = null, CancellationToken cancellationToken = default)
    {
        Error = null;
        try
        {
            ActiveConversation = await _backend.PostAsync<ConversationDetail>("/api/v1/conversations", new CreateConversationRequest(ModelId: modelId), cancellationToken);
            Conversations = await _backend.GetAsync<IReadOnlyList<ConversationSummary>>("/api/v1/conversations", cancellationToken);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        Notify();
    }

    public async Task DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        Error = null;
        try
        {
            await _backend.DeleteAsync($"/api/v1/conversations/{Uri.EscapeDataString(conversationId)}", cancellationToken);
            Conversations = Conversations.Where(c => c.Id != conversationId).ToList();
            if (ActiveConversation?.Id == conversationId)
            {
                ActiveConversation = null;
                DraftResponse = "";
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        Notify();
    }

    public async Task RenameConversationAsync(string conversationId, string newTitle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) return;
        Error = null;
        try
        {
            // Always determine current version to prevent optimistic concurrency / version conflict
            var currentVersion = (ActiveConversation?.Id == conversationId)
                ? ActiveConversation.Version
                : (Conversations.FirstOrDefault(c => c.Id == conversationId)?.Version ?? 1);

            var updated = await _backend.PutAsync<ConversationDetail>(
                $"/api/v1/conversations/{Uri.EscapeDataString(conversationId)}",
                new SaveConversationRequest(Title: newTitle.Trim(), Version: currentVersion),
                cancellationToken);

            Conversations = Conversations.Select(c => c.Id == conversationId ? c with { Title = newTitle.Trim(), Version = updated.Version, Updated = updated.Updated } : c).ToList();
            if (ActiveConversation?.Id == conversationId)
            {
                ActiveConversation = updated;
            }
        }
        catch (ConversationVersionConflictException)
        {
            // Re-fetch latest version to recover cleanly from conflict
            var fresh = await _backend.GetAsync<ConversationDetail>($"/api/v1/conversations/{Uri.EscapeDataString(conversationId)}", cancellationToken);
            var updated = await _backend.PutAsync<ConversationDetail>(
                $"/api/v1/conversations/{Uri.EscapeDataString(conversationId)}",
                new SaveConversationRequest(Title: newTitle.Trim(), Version: fresh.Version),
                cancellationToken);

            Conversations = Conversations.Select(c => c.Id == conversationId ? c with { Title = newTitle.Trim(), Version = updated.Version, Updated = updated.Updated } : c).ToList();
            if (ActiveConversation?.Id == conversationId)
            {
                ActiveConversation = updated;
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        Notify();
    }

    public async Task SendAsync(string text, string? modelId = null, CancellationToken cancellationToken = default)
    {
        if (ActiveConversation is null || string.IsNullOrWhiteSpace(text) || IsStreaming) return;

        Error = null;
        DraftResponse = "";
        CurrentStreamingToolCalls.Clear();
        ActivePermissionRequest = null;
        IsStreaming = true;
        Notify();
        _runCancellation?.Dispose();
        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            ActiveRun = await _backend.PostAsync<RunSummary>(
                $"/api/v1/conversations/{Uri.EscapeDataString(ActiveConversation.Id)}/runs",
                new SendMessageRequest(text.Trim(), modelId, WorkspacePath: WorkspacePath, AllowLocalCapabilities: LocalCapabilitiesEnabled),
                _runCancellation.Token);
            Notify();

            await foreach (var item in _backend.SubscribeToRunAsync(ActiveRun.Id, ct: _runCancellation.Token))
            {
                RunEventReceived?.Invoke(item);
                if (item.EventType.Equals("text.delta", StringComparison.OrdinalIgnoreCase))
                {
                    DraftResponse += item.Payload;
                }
                else if (item.EventType.Equals("run.step", StringComparison.OrdinalIgnoreCase))
                {
                    var delta = ExtractContentDelta(item);
                    if (!string.IsNullOrEmpty(delta)) DraftResponse += delta;

                    var tc = ExtractToolCall(item);
                    if (tc != null)
                    {
                        var existing = CurrentStreamingToolCalls.FirstOrDefault(t => t.Id == tc.Id);
                        if (existing != null)
                        {
                            existing.Result = tc.Result;
                            existing.IsError = tc.IsError;
                        }
                        else
                        {
                            CurrentStreamingToolCalls.Add(tc);
                        }
                    }
                }
                else if (item.EventType.Equals("permission.requested", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        ActivePermissionRequest = JsonSerializer.Deserialize<PendingPermissionRequest>(item.Payload, BackendJson.Options);
                    }
                    catch {}
                }
                else if (item.EventType is "run.completed" or "run.failed" or "run.cancelled")
                {
                    ActivePermissionRequest = null;
                }
                Notify();
            }

            // Re-fetch authoritative latest conversation state after run completion
            var fresh = await _backend.GetAsync<ConversationDetail>(
                $"/api/v1/conversations/{Uri.EscapeDataString(ActiveConversation.Id)}",
                cancellationToken);
            ActiveConversation = fresh;
            if (Conversations.Any(c => c.Id == fresh.Id))
            {
                Conversations = Conversations.Select(c => c.Id == fresh.Id
                    ? c with { Title = fresh.Title, Updated = fresh.Updated, Version = fresh.Version, MessageCount = fresh.Messages.Count }
                    : c).ToList();
            }
            await LoadMemoryAsync(text, cancellationToken);
        }
        catch (OperationCanceledException) when (_runCancellation.IsCancellationRequested)
        {
            Error = "Run cancelled.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsStreaming = false;
            CurrentStreamingToolCalls.Clear();
            ActivePermissionRequest = null;
            Notify();
        }
    }

    public async Task CancelAsync()
    {
        if (ActiveRun is null) return;
        _runCancellation?.Cancel();
        try { await _backend.PostAsync<object>($"/api/v1/runs/{Uri.EscapeDataString(ActiveRun.Id)}/cancel", new CancelRunRequest("Cancelled from desktop")); }
        catch (Exception ex) { Error = ex.Message; }
        Notify();
    }

    public async Task ConfirmMemoryAsync(string memoryId)
    {
        try { await _backend.ConfirmMemoryAsync(memoryId); await RefreshMemoryAsync(); }
        catch (Exception ex) { Error = ex.Message; Notify(); }
    }

    public async Task RejectMemoryAsync(string memoryId)
    {
        try { await _backend.RejectMemoryAsync(memoryId); await RefreshMemoryAsync(); }
        catch (Exception ex) { Error = ex.Message; Notify(); }
    }

    public async Task ForgetMemoryAsync(string memoryId)
    {
        try { await _backend.ForgetMemoryAsync(memoryId); await RefreshMemoryAsync(); }
        catch (Exception ex) { Error = ex.Message; Notify(); }
    }

    private void Notify() => StateChanged?.Invoke();

    private async Task LoadMemoryAsync(string? query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) { RelevantMemories = []; MemoryCandidates = []; return; }
        try
        {
            RelevantMemories = await _backend.SearchMemoriesAsync(query, ct: cancellationToken);
            var all = await _backend.GetMemoriesAsync(cancellationToken);
            MemoryCandidates = all.Where(m => m.Status == StructuredMemoryStatuses.Candidate).Take(10).ToList();
        }
        catch { }
        Notify();
    }

    private async Task RefreshMemoryAsync()
    {
        var all = await _backend.GetMemoriesAsync();
        MemoryCandidates = all.Where(m => m.Status == StructuredMemoryStatuses.Candidate).Take(10).ToList();
        if (ActiveConversation?.Messages.LastOrDefault(m => m.Role == "user")?.Text is { Length: > 0 } query)
            RelevantMemories = await _backend.SearchMemoriesAsync(query, ct: CancellationToken.None);
        Notify();
    }

    private static string? ExtractContentDelta(RunEvent item)
    {
        if (item.EventType.Equals("text.delta", StringComparison.OrdinalIgnoreCase)) return item.Payload;
        if (!item.EventType.Equals("run.step", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var document = JsonDocument.Parse(item.Payload);
            return document.RootElement.TryGetProperty("contentDelta", out var value) ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static ToolCallInfo? ExtractToolCall(RunEvent item)
    {
        try
        {
            using var document = JsonDocument.Parse(item.Payload);
            if (document.RootElement.TryGetProperty("toolCall", out var tcElement) && tcElement.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<ToolCallInfo>(tcElement.GetRawText(), BackendJson.Options);
            }
            return null;
        }
        catch { return null; }
    }
}
