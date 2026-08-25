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
                            Updated = meta.Updated
                        });
                    }
                }
            }

            // 2. If no cloud conversations found or user is guest, check local storage
            if (loadedList.Count == 0)
            {
                if (File.Exists(_legacyConversationsFilePath))
                {
                    await _sessionStorage.MigrateLegacyConversationsAsync(_legacyConversationsFilePath);
                }

                var localMetas = await _sessionStorage.ListAllConversationMetasAsync();
                foreach (var meta in localMetas)
                {
                    var fullConv = await _sessionStorage.LoadFullConversationAsync(meta.Id);
                    loadedList.Add(fullConv);
                }
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

    public async Task SaveConversationAsync(Conversation conversation)
    {
        await _lock.WaitAsync();
        try
        {
            var user = _authService.CurrentUser;
            if (user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.Id))
            {
                await _db.SaveConversationAsync(conversation, user.Id);
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
