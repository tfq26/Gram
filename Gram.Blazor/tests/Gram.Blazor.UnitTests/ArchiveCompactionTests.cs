using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ArchiveCompactionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageService _storageService;
    private readonly SessionStorageService _sessionStorage;
    private readonly SettingsService _settingsService;
    private readonly RepositoryMapService _repoMapService;
    private readonly ContextBuilder _contextBuilder;

    public ArchiveCompactionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "gram_archive_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("GRAM_STORAGE_DIR", _tempDir);

        var config = new ConfigurationBuilder().Build();
        _storageService = new StorageService();
        _sessionStorage = new SessionStorageService(_storageService);
        _settingsService = new SettingsService(config, _storageService);
        _repoMapService = new RepositoryMapService();
        _contextBuilder = new ContextBuilder(_sessionStorage, _settingsService, _repoMapService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch {}
    }

    [Fact]
    public async Task CompactForArchiveAsync_CreatesCompactMemoryCheckpoint()
    {
        var conv = new Conversation("conv-archive-1", "Legacy Architecture", DateTimeOffset.UtcNow);
        conv.Messages.Add(new Message("user", "What database are we using for Gram?"));
        conv.Messages.Add(new Message("assistant", "We use SQLite locally and PostgreSQL in cloud deployment."));
        conv.Messages.Add(new Message("user", "Got it, let's keep SQLite optimized."));

        var checkpoint = await _contextBuilder.CompactForArchiveAsync(conv);

        Assert.False(string.IsNullOrWhiteSpace(checkpoint));
        Assert.Equal(3, conv.ContextSummaryMessageCount);
        Assert.Equal(checkpoint, conv.ContextSummary);

        // Verify it was persisted to disk in conversation directory
        var storedSummary = await _sessionStorage.GetContextSummaryAsync(conv.Id);
        Assert.NotNull(storedSummary);
        Assert.Contains("gram-compacted-through:3", storedSummary);

        // Verify AssembleContextAsync picks up the compacted summary
        var assembled = await _contextBuilder.AssembleContextAsync(conv);
        Assert.Contains("<context_summary>", assembled.SystemPrompt);
        Assert.Contains("SQLite", assembled.SystemPrompt);
    }
}
