using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class StructuredMemoryServiceTests
{
    [Fact]
    public async Task RelationshipMemoryPersistsAndIsRetrievedAcrossServiceInstances()
    {
        using var temp = new TemporaryDirectory();
        var previous = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", temp.Path);
        try
        {
            var first = new StructuredMemoryService();
            var created = await first.RememberAsync(new RememberMemoryRequest(
                "Maya", "is the user's girlfriend", "They enjoy quiet Sunday walks",
                StructuredMemoryKinds.Relationship, SourceConversationId: "conversation-a", Evidence: "User explicitly stated this."));

            var second = new StructuredMemoryService();
            var results = await second.SearchAsync(new MemorySearchRequest("girlfriend quiet walks"));

            Assert.Contains(results, memory => memory.Id == created.Id);
            Assert.Contains("Maya", await second.BuildContextAsync("girlfriend"));
        }
        finally { Environment.SetEnvironmentVariable("GRAM_DATA_DIR", previous); }
    }

    [Fact]
    public async Task NewValueSupersedesOldValueWithoutDeletingHistory()
    {
        using var temp = new TemporaryDirectory();
        var previous = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", temp.Path);
        try
        {
            var service = new StructuredMemoryService();
            var oldValue = await service.RememberAsync(new RememberMemoryRequest("User", "prefers", "dark mode", StructuredMemoryKinds.Preference));
            var newValue = await service.RememberAsync(new RememberMemoryRequest("User", "prefers", "light mode", StructuredMemoryKinds.Preference));

            Assert.Equal(oldValue.Id, newValue.SupersedesId);
            Assert.Contains((await service.ListAsync(includeInactive: true)), m => m.Id == oldValue.Id && m.Status == StructuredMemoryStatuses.Superseded);
            Assert.Contains((await service.SearchAsync(new MemorySearchRequest("prefers"))), m => m.Id == newValue.Id);
        }
        finally { Environment.SetEnvironmentVariable("GRAM_DATA_DIR", previous); }
    }

    [Fact]
    public async Task CandidateMemoryIsExcludedUntilExplicitlyIncluded()
    {
        using var temp = new TemporaryDirectory();
        var previous = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", temp.Path);
        try
        {
            var service = new StructuredMemoryService();
            var candidate = await service.RememberAsync(new RememberMemoryRequest("Maya", "may be", "the user's colleague", RequireConfirmation: true));
            Assert.DoesNotContain(await service.SearchAsync(new MemorySearchRequest("Maya colleague")), m => m.Id == candidate.Id);
            Assert.Contains(await service.SearchAsync(new MemorySearchRequest("Maya colleague", IncludeCandidates: true)), m => m.Id == candidate.Id);
            Assert.True(await service.ForgetAsync(candidate.Id));
        }
        finally { Environment.SetEnvironmentVariable("GRAM_DATA_DIR", previous); }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gram-structured-memory-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
