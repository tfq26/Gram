using System.Text.Json;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class MentionResolverTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _workspace;
    private readonly SessionStorageService _sessionStorage;
    private readonly MentionResolver _resolver;

    public MentionResolverTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "gram_mentions_" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_testRoot, "workspace");
        Directory.CreateDirectory(_workspace);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDir"] = Path.Combine(_testRoot, "data") })
            .Build();
        var storage = new StorageService(configuration);
        _sessionStorage = new SessionStorageService(storage);
        _resolver = new MentionResolver(new ToolboxManagerService(storage), _sessionStorage);
    }

    [Fact]
    public async Task FileMention_IncludesOnlyAuthorizedConversationAttachment()
    {
        var conversation = new Conversation("authorized-files", "Authorized files", DateTimeOffset.UtcNow);
        await using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("public sealed class BillingService { }"));
        var attachment = await _sessionStorage.SaveAttachmentAsync(conversation.Id, "BillingService.cs", content, "text/plain");
        conversation.Attachments.Add(attachment);
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "secret.txt"), "must-not-leak");

        var mentions = new List<MentionReference>
        {
            new() { Id = "inside", Kind = MentionKind.File, Label = "BillingService.cs", Target = "BillingService.cs" },
            new() { Id = "outside", Kind = MentionKind.File, Label = "secret.txt", Target = "../secret.txt" }
        };

        var result = await _resolver.ResolveAsync(mentions, conversation, _workspace, "Review this service");

        Assert.Contains("mentioned_file", result.Context);
        Assert.Contains("source=\"user_attachment\"", result.Context);
        Assert.Contains("BillingService", result.Context);
        Assert.DoesNotContain("must-not-leak", result.Context);
    }

    [Fact]
    public async Task WebMention_ForcesSearchUsingVisibleUserPrompt()
    {
        var mentions = new List<MentionReference>
        {
            new() { Id = "web", Kind = MentionKind.Web, Label = "Search the web", Target = "web" }
        };

        var result = await _resolver.ResolveAsync(
            mentions,
            new Conversation(),
            _workspace,
            "What happened in technology today?");

        Assert.Equal("What happened in technology today?", result.ForcedWebSearchQuery);
    }

    [Fact]
    public async Task ContextBuilder_WrapsMentionDataAsUntrustedContext()
    {
        var storage = new StorageService();
        var sessionStorage = new SessionStorageService(storage);
        var settings = new SettingsService(new ConfigurationBuilder().Build(), storage);
        var builder = new ContextBuilder(sessionStorage, settings, new RepositoryMapService());

        var result = await builder.AssembleContextAsync(
            new Conversation("mention-test", "Mention test", DateTimeOffset.UtcNow),
            mentionedContext: "<mentioned_file path=\"README.md\">Reference text</mentioned_file>");

        Assert.Contains("<mentioned_context>", result.SystemPrompt);
        Assert.Contains("untrusted reference data", result.SystemPrompt);
        Assert.Contains("README.md", result.SystemPrompt);
    }

    [Fact]
    public void MentionMetadata_RoundTripsWithMessages()
    {
        var message = new Message("user", "Review this")
        {
            Mentions =
            [
                new MentionReference
                {
                    Id = "file:Pages/Home.razor",
                    Kind = MentionKind.File,
                    Label = "Home.razor",
                    Target = "Pages/Home.razor"
                }
            ]
        };

        var restored = JsonSerializer.Deserialize<Message>(JsonSerializer.Serialize(message));

        Assert.NotNull(restored);
        var mention = Assert.Single(restored.Mentions);
        Assert.Equal(MentionKind.File, mention.Kind);
        Assert.Equal("Pages/Home.razor", mention.Target);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
        }
    }
}
