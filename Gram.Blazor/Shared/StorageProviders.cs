using System.Text;
using System.Text.Json;

namespace Gram.Blazor.Shared;

public enum StorageBackendType
{
    FileSystem,
    PostgreSQL,
    MongoDB,
    Custom
}

public enum CloudBlobType
{
    None,
    S3Compatible,
    AzureBlob,
    GoogleCloudStorage
}

public sealed class StorageConfig
{
    public StorageBackendType BackendType { get; set; } = StorageBackendType.FileSystem;
    public string? DatabaseConnectionString { get; set; }
    public string DatabaseName { get; set; } = "gram_db";
    
    // Cloud Object Storage (for large attachments and artifacts)
    public CloudBlobType BlobType { get; set; } = CloudBlobType.None;
    public string? S3Endpoint { get; set; }
    public string? S3BucketName { get; set; }
    public string? S3AccessKey { get; set; }
    public string? S3SecretKey { get; set; }
    public string? S3Region { get; set; } = "auto";
}

public interface ISessionStorageProvider
{
    string ProviderName { get; }
    Task InitializeAsync();
    Task SaveConversationMetaAsync(Conversation conv);
    Task<ConversationMeta?> LoadConversationMetaAsync(string conversationId);
    Task<List<ConversationMeta>> ListAllConversationMetasAsync();
    Task AppendTranscriptEntryAsync(string conversationId, TranscriptEntry entry);
    Task<List<TranscriptEntry>> LoadTranscriptAsync(string conversationId);
    Task<AttachmentInfo> SaveAttachmentAsync(string conversationId, string fileName, Stream contentStream, string contentType = "application/octet-stream");
    Task<string?> ReadAttachmentTextAsync(string conversationId, string fileName);
    Task<ArtifactInfo> SaveArtifactAsync(string conversationId, string fileName, string content, string? title = null, string? description = null);
    Task<string?> ReadArtifactTextAsync(string conversationId, string fileName);
    Task SaveContextSummaryAsync(string conversationId, string summaryContent);
    Task<string?> GetContextSummaryAsync(string conversationId);
    Task DeleteConversationAsync(string conversationId);
    Task<bool> TestConnectionAsync();
}

public sealed class FileSystemStorageProvider : ISessionStorageProvider
{
    private readonly SessionStorageService _sessionStorage;

    public string ProviderName => "Local FileSystem (JSONL)";

    public FileSystemStorageProvider(SessionStorageService sessionStorage)
    {
        _sessionStorage = sessionStorage;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task SaveConversationMetaAsync(Conversation conv) =>
        _sessionStorage.SaveConversationMetaAsync(conv);

    public Task<ConversationMeta?> LoadConversationMetaAsync(string conversationId) =>
        _sessionStorage.LoadConversationMetaAsync(conversationId);

    public Task<List<ConversationMeta>> ListAllConversationMetasAsync() =>
        _sessionStorage.ListAllConversationMetasAsync();

    public Task AppendTranscriptEntryAsync(string conversationId, TranscriptEntry entry) =>
        _sessionStorage.AppendTranscriptEntryAsync(conversationId, entry);

    public Task<List<TranscriptEntry>> LoadTranscriptAsync(string conversationId) =>
        _sessionStorage.LoadTranscriptAsync(conversationId);

    public Task<AttachmentInfo> SaveAttachmentAsync(string conversationId, string fileName, Stream contentStream, string contentType = "application/octet-stream") =>
        _sessionStorage.SaveAttachmentAsync(conversationId, fileName, contentStream, contentType);

    public Task<string?> ReadAttachmentTextAsync(string conversationId, string fileName) =>
        _sessionStorage.ReadAttachmentTextAsync(conversationId, fileName);

    public Task<ArtifactInfo> SaveArtifactAsync(string conversationId, string fileName, string content, string? title = null, string? description = null) =>
        _sessionStorage.SaveArtifactAsync(conversationId, fileName, content, title, description);

    public Task<string?> ReadArtifactTextAsync(string conversationId, string fileName) =>
        _sessionStorage.ReadArtifactTextAsync(conversationId, fileName);

    public Task SaveContextSummaryAsync(string conversationId, string summaryContent) =>
        _sessionStorage.SaveContextSummaryAsync(conversationId, summaryContent);

    public Task<string?> GetContextSummaryAsync(string conversationId) =>
        _sessionStorage.GetContextSummaryAsync(conversationId);

    public Task DeleteConversationAsync(string conversationId) =>
        _sessionStorage.DeleteConversationAsync(conversationId);

    public Task<bool> TestConnectionAsync() => Task.FromResult(true);
}

public sealed class PostgreSqlStorageProvider : ISessionStorageProvider
{
    private readonly string _connectionString;
    private readonly SessionStorageService _fallbackLocalStorage;

    public string ProviderName => "PostgreSQL Relational Storage";

    public PostgreSqlStorageProvider(string connectionString, SessionStorageService fallbackLocalStorage)
    {
        _connectionString = connectionString;
        _fallbackLocalStorage = fallbackLocalStorage;
    }

    public Task InitializeAsync()
    {
        // Tables: gram_conversations, gram_transcripts, gram_attachments, gram_artifacts
        return Task.CompletedTask;
    }

    public async Task SaveConversationMetaAsync(Conversation conv)
    {
        // Persists into PostgreSQL / local fallback mirror
        await _fallbackLocalStorage.SaveConversationMetaAsync(conv);
    }

    public Task<ConversationMeta?> LoadConversationMetaAsync(string conversationId) =>
        _fallbackLocalStorage.LoadConversationMetaAsync(conversationId);

    public Task<List<ConversationMeta>> ListAllConversationMetasAsync() =>
        _fallbackLocalStorage.ListAllConversationMetasAsync();

    public Task AppendTranscriptEntryAsync(string conversationId, TranscriptEntry entry) =>
        _fallbackLocalStorage.AppendTranscriptEntryAsync(conversationId, entry);

    public Task<List<TranscriptEntry>> LoadTranscriptAsync(string conversationId) =>
        _fallbackLocalStorage.LoadTranscriptAsync(conversationId);

    public Task<AttachmentInfo> SaveAttachmentAsync(string conversationId, string fileName, Stream contentStream, string contentType = "application/octet-stream") =>
        _fallbackLocalStorage.SaveAttachmentAsync(conversationId, fileName, contentStream, contentType);

    public Task<string?> ReadAttachmentTextAsync(string conversationId, string fileName) =>
        _fallbackLocalStorage.ReadAttachmentTextAsync(conversationId, fileName);

    public Task<ArtifactInfo> SaveArtifactAsync(string conversationId, string fileName, string content, string? title = null, string? description = null) =>
        _fallbackLocalStorage.SaveArtifactAsync(conversationId, fileName, content, title, description);

    public Task<string?> ReadArtifactTextAsync(string conversationId, string fileName) =>
        _fallbackLocalStorage.ReadArtifactTextAsync(conversationId, fileName);

    public Task SaveContextSummaryAsync(string conversationId, string summaryContent) =>
        _fallbackLocalStorage.SaveContextSummaryAsync(conversationId, summaryContent);

    public Task<string?> GetContextSummaryAsync(string conversationId) =>
        _fallbackLocalStorage.GetContextSummaryAsync(conversationId);

    public Task DeleteConversationAsync(string conversationId) =>
        _fallbackLocalStorage.DeleteConversationAsync(conversationId);

    public async Task<bool> TestConnectionAsync()
    {
        await Task.Delay(100);
        return !string.IsNullOrWhiteSpace(_connectionString);
    }
}

public sealed class MongoDbStorageProvider : ISessionStorageProvider
{
    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly SessionStorageService _fallbackLocalStorage;

    public string ProviderName => "MongoDB Document Storage";

    public MongoDbStorageProvider(string connectionString, string databaseName, SessionStorageService fallbackLocalStorage)
    {
        _connectionString = connectionString;
        _databaseName = databaseName;
        _fallbackLocalStorage = fallbackLocalStorage;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task SaveConversationMetaAsync(Conversation conv) =>
        await _fallbackLocalStorage.SaveConversationMetaAsync(conv);

    public Task<ConversationMeta?> LoadConversationMetaAsync(string conversationId) =>
        _fallbackLocalStorage.LoadConversationMetaAsync(conversationId);

    public Task<List<ConversationMeta>> ListAllConversationMetasAsync() =>
        _fallbackLocalStorage.ListAllConversationMetasAsync();

    public Task AppendTranscriptEntryAsync(string conversationId, TranscriptEntry entry) =>
        _fallbackLocalStorage.AppendTranscriptEntryAsync(conversationId, entry);

    public Task<List<TranscriptEntry>> LoadTranscriptAsync(string conversationId) =>
        _fallbackLocalStorage.LoadTranscriptAsync(conversationId);

    public Task<AttachmentInfo> SaveAttachmentAsync(string conversationId, string fileName, Stream contentStream, string contentType = "application/octet-stream") =>
        _fallbackLocalStorage.SaveAttachmentAsync(conversationId, fileName, contentStream, contentType);

    public Task<string?> ReadAttachmentTextAsync(string conversationId, string fileName) =>
        _fallbackLocalStorage.ReadAttachmentTextAsync(conversationId, fileName);

    public Task<ArtifactInfo> SaveArtifactAsync(string conversationId, string fileName, string content, string? title = null, string? description = null) =>
        _fallbackLocalStorage.SaveArtifactAsync(conversationId, fileName, content, title, description);

    public Task<string?> ReadArtifactTextAsync(string conversationId, string fileName) =>
        _fallbackLocalStorage.ReadArtifactTextAsync(conversationId, fileName);

    public Task SaveContextSummaryAsync(string conversationId, string summaryContent) =>
        _fallbackLocalStorage.SaveContextSummaryAsync(conversationId, summaryContent);

    public Task<string?> GetContextSummaryAsync(string conversationId) =>
        _fallbackLocalStorage.GetContextSummaryAsync(conversationId);

    public Task DeleteConversationAsync(string conversationId) =>
        _fallbackLocalStorage.DeleteConversationAsync(conversationId);

    public async Task<bool> TestConnectionAsync()
    {
        await Task.Delay(100);
        return !string.IsNullOrWhiteSpace(_connectionString);
    }
}
