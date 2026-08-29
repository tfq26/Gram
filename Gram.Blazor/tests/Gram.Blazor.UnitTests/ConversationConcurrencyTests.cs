using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ConversationConcurrencyTests : IDisposable
{
    private readonly string _directory;
    private readonly string? _previousDataDirectory;
    private readonly string? _previousStorageDirectory;

    public ConversationConcurrencyTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "gram_concurrency_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previousDataDirectory = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
        _previousStorageDirectory = Environment.GetEnvironmentVariable("GRAM_STORAGE_DIR");
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", _directory);
    }

    [Fact]
    public async Task SaveConversation_RejectsStaleExpectedVersion()
    {
        var storage = new StorageService();
        var database = new SqliteDatabaseService(storage);
        await database.InitializeAsync();
        var user = await database.CreateUserAsync("version@example.com", "Version Test", "Password123!");

        var first = new Conversation { Id = "versioned", Title = "First", Version = 2 };
        Assert.True(await database.SaveConversationAsync(first, user.Id, expectedVersion: 1));

        var stale = new Conversation { Id = first.Id, Title = "Stale", Version = 3 };
        Assert.False(await database.SaveConversationAsync(stale, user.Id, expectedVersion: 1));

        var current = new Conversation { Id = first.Id, Title = "Current", Version = 3 };
        Assert.True(await database.SaveConversationAsync(current, user.Id, expectedVersion: 2));
        Assert.Equal("Current", (await database.GetConversationAsync(first.Id, user.Id))?.Title);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { }
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", _previousDataDirectory);
        Environment.SetEnvironmentVariable("GRAM_STORAGE_DIR", _previousStorageDirectory);
    }
}
