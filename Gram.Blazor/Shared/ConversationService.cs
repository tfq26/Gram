using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class ConversationService
{
    private readonly StorageService _storageService;
    private readonly SessionStorageService _sessionStorage;
    private readonly string _legacyConversationsFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public List<Conversation> Conversations { get; private set; } = new();
    public event Action? OnChange;

    public ConversationService(StorageService storageService, SessionStorageService sessionStorage)
    {
        _storageService = storageService;
        _sessionStorage = sessionStorage;
        _legacyConversationsFilePath = _storageService.GetFilePath("conversations.json");
        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            // 1. Check if legacy migration is needed
            if (File.Exists(_legacyConversationsFilePath))
            {
                await _sessionStorage.MigrateLegacyConversationsAsync(_legacyConversationsFilePath);
            }

            // 2. Load all conversation metas
            var metas = await _sessionStorage.ListAllConversationMetasAsync();
            var loadedList = new List<Conversation>();

            foreach (var meta in metas)
            {
                var fullConv = await _sessionStorage.LoadFullConversationAsync(meta.Id);
                loadedList.Add(fullConv);
            }

            if (loadedList.Count > 0)
            {
                Conversations = loadedList;
            }
            else
            {
                var defaultConv = new Conversation("default", "New chat", DateTimeOffset.UtcNow);
                await _sessionStorage.SaveConversationMetaAsync(defaultConv);
                Conversations = [defaultConv];
            }

            OnChange?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConversationService] Error initializing conversations: {ex.Message}");
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
            foreach (var conv in Conversations)
            {
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
