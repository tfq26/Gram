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

    [Fact]
    public async Task PresentationMention_FlagsPresentationModeAndAppendsInstruction()
    {
        var mentions = new List<MentionReference>
        {
            new() { Id = "presentation", Kind = MentionKind.Presentation, Label = "Presentation Deck", Target = "presentation" }
        };

        var result = await _resolver.ResolveAsync(
            mentions,
            new Conversation(),
            _workspace,
            "Autonomous Agents in Production");

        Assert.True(result.IsPresentationRequested);
        Assert.Contains("presentation_mode", result.Context);
        Assert.Contains("create_presentation_project", result.Context);
    }

    [Fact]
    public void IsPresentationRequest_DifferentiatesCreationIntentVsConversationalQuery()
    {
        // Explicit creation intents
        Assert.True(PresentationGenerationService.IsPresentationRequest("Create a presentation about quantum computing"));
        Assert.True(PresentationGenerationService.IsPresentationRequest("Make me a slide deck on renewable energy"));
        Assert.True(PresentationGenerationService.IsPresentationRequest("Generate slides about distributed systems"));
        Assert.True(PresentationGenerationService.IsPresentationRequest("@presentation Machine Learning Overview"));

        // Conversational queries mentioning presentation (should NOT trigger auto-generation)
        Assert.False(PresentationGenerationService.IsPresentationRequest("What makes a good presentation?"));
        Assert.False(PresentationGenerationService.IsPresentationRequest("Can we talk about the presentation structure?"));
        Assert.False(PresentationGenerationService.IsPresentationRequest("How many slides are usually in a pitch deck?"));
        Assert.False(PresentationGenerationService.IsPresentationRequest("I gave a presentation yesterday and wanted to discuss it"));
    }

    [Fact]
    public async Task PresentationGenerationService_PersistsAndLoadsProjectsFromDisk()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDir"] = Path.Combine(_testRoot, "pres_data") })
            .Build();
        var storage = new StorageService(config);
        var subagentEngine = new FakeSubagentEngine();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<PresentationGenerationService>();

        // 1. Create service and a project
        var service1 = new PresentationGenerationService(subagentEngine, storage, logger);
        var project = await service1.CreatePresentationAsync(new PresentationBrief
        {
            Title = "Quantum Computing Future",
            Audience = "Engineers",
            CommunicationJob = "Explain Quantum Supremacy",
            DesignSystem = "Minimalist Dark",
            TargetSlideCount = 5
        }, "conv-quantum-101", _workspace);

        Assert.NotNull(project);
        Assert.Equal(5, project.Sections.Count);

        // 2. Instantiate a second service instance (simulating page refresh or restart)
        var service2 = new PresentationGenerationService(subagentEngine, storage, logger);
        var loaded = service2.GetProjects("conv-quantum-101");

        Assert.Single(loaded);
        Assert.Equal(project.Id, loaded[0].Id);
        Assert.Equal("Quantum Computing Future", loaded[0].Title);
        Assert.Equal(5, loaded[0].Sections.Count);
    }

    private sealed class FakeSubagentEngine : ISubagentEngine
    {
        #pragma warning disable CS0067
        public event Action<SubagentTask>? OnTaskUpdated;
        #pragma warning restore CS0067
        public IReadOnlyList<SubagentDefinition> GetDefinitions() => Array.Empty<SubagentDefinition>();
        public SubagentDefinition? GetDefinition(string name) => null;
        public SubagentDefinition? ResolveProfile(string capability, string? preferredProfileId = null) => null;
        public void RegisterDefinition(SubagentDefinition definition) { }
        public Task<SubagentTask> SpawnTaskAsync(SubagentTaskSpec spec, CancellationToken cancellationToken = default)
        {
            var task = new SubagentTask
            {
                Id = "task-" + Guid.NewGuid().ToString("N")[..8],
                ParentConversationId = spec.ParentConversationId,
                SectionId = spec.SectionId,
                Status = SubagentTaskStatus.Running
            };
            return Task.FromResult(task);
        }
        public Task<SubagentTask> SpawnSubagentAsync(string parentConversationId, string subagentName, string role, string prompt, string workspacePath, bool? useSandbox = null, string? modelId = null, CancellationToken cancellationToken = default)
        {
            var task = new SubagentTask
            {
                Id = "task-" + Guid.NewGuid().ToString("N")[..8],
                ParentConversationId = parentConversationId,
                Status = SubagentTaskStatus.Running
            };
            return Task.FromResult(task);
        }
        public SubagentTask? GetTask(string taskId) => null;
        public IReadOnlyList<SubagentTask> GetTasksForConversation(string parentConversationId) => Array.Empty<SubagentTask>();
        public Task<bool> CancelTaskAsync(string taskId) => Task.FromResult(true);
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
