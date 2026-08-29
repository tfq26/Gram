using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed record SandboxAuditEntry(
    DateTimeOffset Timestamp,
    string Event,
    string SandboxId,
    string? RunId = null,
    string? Command = null,
    bool? Success = null,
    long? DurationMs = null,
    string? Error = null,
    string? EnforcementMode = null);

public sealed class SandboxAuditService
{
    private readonly string _path;
    private readonly ILogger<SandboxAuditService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string LogPath => _path;

    public SandboxAuditService(StorageService storage, ILogger<SandboxAuditService> logger)
    {
        _path = storage.GetFilePath("sandbox_audit.jsonl");
        _logger = logger;
    }

    public async Task RecordAsync(SandboxAuditEntry entry, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try { await File.AppendAllTextAsync(_path, JsonSerializer.Serialize(entry) + Environment.NewLine, cancellationToken); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not persist sandbox audit event {Event} for {SandboxId}.", entry.Event, entry.SandboxId); }
        finally { _lock.Release(); }
    }
}
