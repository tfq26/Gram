using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ContextOptimizationUnitTests
{
    [Fact]
    public void PromptCacheKeyIsStableWithoutExposingWorkspacePath()
    {
        var first = PromptCacheKey.ForAgent("gpt-4o", "/workspace/project");
        var second = PromptCacheKey.ForAgent("gpt-4o", "/workspace/project");

        Assert.Equal(first, second);
        Assert.StartsWith("gram-agent-v1:", first);
        Assert.DoesNotContain("/workspace/project", first);
    }

    [Fact]
    public void ToolResultsAreReducedOnlyForTheModelProjection()
    {
        var source = new string('a', 10_000);

        var reduced = ToolResultReducer.ForModel("run_command", source);

        Assert.True(reduced.Length < source.Length);
        Assert.Contains("tool output reduced", reduced, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(new string('a', 10), reduced);
        Assert.EndsWith(new string('a', 10), reduced);
    }

    [Fact]
    public async Task ContextBuilderCreatesAndPersistsACompactionCheckpoint()
    {
        using var data = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", data.Path);

        var storage = new StorageService();
        var sessionStorage = new SessionStorageService(storage);
        var settings = new SettingsService(new ConfigurationBuilder().Build(), storage);
        var builder = new ContextBuilder(sessionStorage, settings, new RepositoryMapService());
        var conversation = new Conversation("compact-test", "Compaction Test", DateTimeOffset.UtcNow);

        for (var i = 0; i < 24; i++)
        {
            conversation.Messages.Add(new Message(i % 2 == 0 ? "user" : "assistant", $"message-{i} important decision"));
        }

        var compacted = await builder.EnsureContextSummaryAsync(conversation, preserveRecentMessages: 8);
        var assembled = await builder.AssembleContextAsync(conversation, maxRecentMessages: 8);

        Assert.True(compacted);
        Assert.True(conversation.ContextSummaryMessageCount >= 16);
        Assert.Contains("message-0", conversation.ContextSummary);
        Assert.Contains("message-23", assembled.Messages[^1].Text);
        Assert.DoesNotContain(assembled.Messages, message => message.Text.Contains("message-0", StringComparison.Ordinal));

        var persisted = await sessionStorage.GetContextSummaryAsync(conversation.Id);
        Assert.Contains("gram-compacted-through", persisted);
    }

    [Fact]
    public void GenerateFallbackTitle_ExtractsConciseMeaningfulTitle()
    {
        var title1 = ModelService.GenerateFallbackTitle("Can you please help me configure PostgreSQL indexes with EF Core in .NET 9?");
        Assert.Equal("Configure PostgreSQL indexes with EF", title1);

        var title2 = ModelService.GenerateFallbackTitle("how do i build a multi-agent orchestration workflow?");
        Assert.Equal("Multi-agent orchestration workflow", title2);

        var title3 = ModelService.GenerateFallbackTitle("tell me about quantum computing algorithms");
        Assert.Equal("Quantum computing algorithms", title3);

        var title4 = ModelService.GenerateFallbackTitle("");
        Assert.Equal("New chat", title4);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gram-context-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
