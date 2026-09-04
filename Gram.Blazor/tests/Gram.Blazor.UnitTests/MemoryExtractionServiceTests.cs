using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class MemoryExtractionServiceTests
{
    [Fact]
    public async Task ExplicitRememberCreatesActiveRelationshipMemory()
    {
        using var temp = new TemporaryDirectory();
        var old = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", temp.Path);
        try
        {
            var service = new StructuredMemoryService();
            var extracted = await new MemoryExtractionService(service).ExtractAsync("Remember that my girlfriend is named Maya", "conversation-1");
            var memory = Assert.Single(extracted);
            Assert.Equal(StructuredMemoryStatuses.Active, memory.Status);
            Assert.Equal(StructuredMemoryKinds.Relationship, memory.Kind);
            Assert.Equal("Maya", memory.Value);
        }
        finally { Environment.SetEnvironmentVariable("GRAM_DATA_DIR", old); }
    }

    [Fact]
    public async Task InferredPreferenceRequiresConfirmationBeforeRetrieval()
    {
        using var temp = new TemporaryDirectory();
        var old = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", temp.Path);
        try
        {
            var service = new StructuredMemoryService();
            var memory = Assert.Single(await new MemoryExtractionService(service).ExtractAsync("I prefer dark mode"));
            Assert.Equal(StructuredMemoryStatuses.Candidate, memory.Status);
            Assert.Empty(await service.SearchAsync(new MemorySearchRequest("dark mode")));
            Assert.True(await service.ConfirmAsync(memory.Id));
            Assert.Contains(await service.SearchAsync(new MemorySearchRequest("dark mode")), item => item.Id == memory.Id);
        }
        finally { Environment.SetEnvironmentVariable("GRAM_DATA_DIR", old); }
    }

    [Fact]
    public async Task MemoryFromOneConversationIsAvailableToASeparateConversation()
    {
        using var temp = new TemporaryDirectory();
        var old = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", temp.Path);
        try
        {
            var service = new StructuredMemoryService();
            var extractor = new MemoryExtractionService(service);
            await extractor.ExtractAsync("Remember that my girlfriend is named Maya", "conversation-a");

            // Conversation B supplies only a relevant query; it does not share
            // Conversation A's messages or transcript.
            var context = await service.BuildContextAsync("Who is my girlfriend?", "default");
            Assert.Contains("Maya", context);
            Assert.DoesNotContain("conversation-a", context);
        }
        finally { Environment.SetEnvironmentVariable("GRAM_DATA_DIR", old); }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gram-memory-extract-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
