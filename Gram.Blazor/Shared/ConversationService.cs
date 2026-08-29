using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class ConversationService
{
    private readonly StorageService _storageService;
    private readonly SessionStorageService _sessionStorage;
    private readonly IDatabaseProvider _db;
    private readonly IAuthService _authService;
    private readonly string _legacyConversationsFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public List<Conversation> Conversations { get; private set; } = new();
    public event Action? OnChange;

    public ConversationService(
        StorageService storageService, 
        SessionStorageService sessionStorage,
        IDatabaseProvider db,
        IAuthService authService)
    {
        _storageService = storageService;
        _sessionStorage = sessionStorage;
        _db = db;
        _authService = authService;
        _legacyConversationsFilePath = _storageService.GetFilePath("conversations.json");

        _authService.OnAuthStateChanged += () =>
        {
            _ = LoadConversationsAsync();
        };

        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        await LoadConversationsAsync();
    }

    public async Task LoadConversationsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var user = _authService.CurrentUser;
            var loadedList = new List<Conversation>();
            var loadedFromLocalStorage = false;

            if (user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.Id))
            {
                // 1. Load from Cloud Database (PostgreSQL / SQLite)
                var metas = await _db.ListConversationsAsync(user.Id);
                foreach (var meta in metas)
                {
                    var fullConv = await _db.GetConversationAsync(meta.Id, user.Id);
                    if (fullConv != null)
                    {
                        loadedList.Add(fullConv);
                    }
                    else
                    {
                        loadedList.Add(new Conversation(meta.Id, meta.Title, meta.Created)
                        {
                            ModelId = meta.ModelId,
                            SystemPromptOverride = meta.SystemPromptOverride,
                            Rules = meta.Rules,
                            Updated = meta.Updated,
                            Version = meta.Version
                        });
                    }
                }
            }

            // 2. If no cloud conversations found or user is guest, check local storage
            if (loadedList.Count == 0)
            {
                if (File.Exists(_legacyConversationsFilePath) || Directory.Exists(_storageService.GetFilePath("conversations")))
                {
                    BackupLegacyStorage();
                    await _sessionStorage.MigrateLegacyConversationsAsync(_legacyConversationsFilePath);
                }

                var localMetas = await _sessionStorage.ListAllConversationMetasAsync();
                foreach (var meta in localMetas)
                {
                    var fullConv = await _sessionStorage.LoadFullConversationAsync(meta.Id);
                    loadedList.Add(fullConv);
                }
                loadedFromLocalStorage = loadedList.Count > 0;
            }

            if (loadedFromLocalStorage && user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.Id))
            {
                await ImportLocalConversationsAsync(loadedList, user.Id);
            }

            if (loadedList.Count > 0)
            {
                Conversations = loadedList;
            }
            else
            {
                var defaultConv = new Conversation("default", "New chat", DateTimeOffset.UtcNow);
                Conversations = [defaultConv];
                if (user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.Id))
                {
                    await _db.SaveConversationAsync(defaultConv, user.Id);
                }
                await _sessionStorage.SaveConversationMetaAsync(defaultConv);
            }

            OnChange?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConversationService] Error loading conversations: {ex.Message}");
            if (Conversations.Count == 0)
            {
                Conversations = [new Conversation("default", "New chat", DateTimeOffset.UtcNow)];
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ImportLocalConversationsAsync(IReadOnlyList<Conversation> conversations, string userId)
    {
        var migrationDirectory = _storageService.GetFilePath("migrations");
        Directory.CreateDirectory(migrationDirectory);
        var markerPath = Path.Combine(migrationDirectory, $"conversations-v1-{Sanitize(userId)}.json");
        if (File.Exists(markerPath)) return;

        var imported = 0;
        foreach (var conversation in conversations)
        {
            if (await _db.SaveConversationAsync(conversation, userId)) imported++;
            else throw new IOException($"Could not import legacy conversation '{conversation.Id}'.");
        }

        await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(new
        {
            completed = true,
            completedAt = DateTimeOffset.UtcNow,
            sourceFormat = "legacy-local-storage",
            importedConversationCount = imported
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void BackupLegacyStorage()
    {
        try
        {
            var backupRoot = _storageService.GetFilePath("backups");
            if (Directory.Exists(backupRoot) && Directory.GetDirectories(backupRoot).Length > 0) return;
            var backupDirectory = Path.Combine(backupRoot, $"pre-backend-migration-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
            Directory.CreateDirectory(backupDirectory);
            if (File.Exists(_legacyConversationsFilePath))
                File.Copy(_legacyConversationsFilePath, Path.Combine(backupDirectory, "conversations.json"), true);
            var localDirectory = _storageService.GetFilePath("conversations");
            if (Directory.Exists(localDirectory)) CopyDirectory(localDirectory, Path.Combine(backupDirectory, "conversations"));
        }
        catch (Exception ex)
        {
            // A backup failure must stop migration rather than risk a partial import.
            throw new IOException("Could not create the pre-migration backup.", ex);
        }
    }

    private static string Sanitize(string value) => string.Concat(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    public async Task SaveConversationAsync(Conversation conversation)
    {
        await _lock.WaitAsync();
        try
        {
            var user = _authService.CurrentUser;
            var expectedVersion = conversation.Version;
            conversation.Version = Math.Max(1, expectedVersion + 1);
            if (user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.Id))
            {
                if (!await _db.SaveConversationAsync(conversation, user.Id, expectedVersion: expectedVersion))
                    throw new ConversationVersionConflictException(expectedVersion);
            }
            await _sessionStorage.SaveConversationMetaAsync(conversation);
            OnChange?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConversationService] Error saving conversation {conversation.Id}: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveConversationsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var user = _authService.CurrentUser;
            foreach (var conv in Conversations)
            {
                if (user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.Id))
                {
                    await _db.SaveConversationAsync(conv, user.Id);
                }
                await _sessionStorage.SaveConversationMetaAsync(conv);
            }
            OnChange?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConversationService] Error batch saving conversations: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteConversationAsync(string conversationId)
    {
        await _lock.WaitAsync();
        try
        {
            var index = Conversations.FindIndex(c => c.Id == conversationId);
            if (index >= 0)
            {
                Conversations.RemoveAt(index);
                var user = _authService.CurrentUser;
                if (user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.Id))
                {
                    await _db.DeleteConversationAsync(conversationId, user.Id);
                }
                await _sessionStorage.DeleteConversationAsync(conversationId);
                OnChange?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConversationService] Error deleting conversation {conversationId}: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AppendTranscriptAsync(string conversationId, TranscriptEntry entry)
    {
        await _sessionStorage.AppendTranscriptEntryAsync(conversationId, entry);
    }
}
