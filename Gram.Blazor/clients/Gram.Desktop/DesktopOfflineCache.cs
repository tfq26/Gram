using System.Text.Json;
using Gram.Blazor.Shared;

namespace Gram.Desktop;

/// <summary>Small, local-only cache for desktop recovery. It never stores tokens or provider data.</summary>
public sealed class DesktopOfflineCache
{
    private readonly string _path;
    private readonly object _gate = new();

    public DesktopOfflineCache(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gram", "desktop-cache.json");
    }

    public string Path => _path;

    public DesktopCacheSnapshot Read()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return new();
                return JsonSerializer.Deserialize<DesktopCacheSnapshot>(File.ReadAllText(_path), BackendJson.Options) ?? new();
            }
            catch (JsonException) { return new(); }
            catch (IOException) { return new(); }
        }
    }

    public void Write(DesktopCacheSnapshot snapshot)
    {
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, BackendJson.Options));
            File.Move(temp, _path, true);
        }
    }
}

public sealed record DesktopCacheSnapshot(
    IReadOnlyList<ConversationSummary>? Conversations = null,
    IReadOnlyList<ModelSummary>? Models = null,
    IReadOnlyList<ConversationDetail>? ConversationDetails = null,
    RunRecoveryState? ActiveRun = null,
    DateTimeOffset? UpdatedAt = null)
{
    public IReadOnlyList<ConversationSummary> SafeConversations => Conversations ?? [];
    public IReadOnlyList<ModelSummary> SafeModels => Models ?? [];
    public IReadOnlyList<ConversationDetail> SafeConversationDetails => ConversationDetails ?? [];
}

public sealed record RunRecoveryState(string RunId, string ConversationId, long LastSequence, DateTimeOffset SavedAt);
