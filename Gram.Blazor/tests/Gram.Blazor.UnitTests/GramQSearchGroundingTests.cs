using System.Net;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class GramQSearchGroundingTests
{
    [Fact]
    public void ModelService_ContainsCorrectEnginesForGramQAndGramQPlus()
    {
        var modelService = new ModelService();
        var gramQ = modelService.Models.FirstOrDefault(m => m.Id == "gram-q");
        var gramQPlus = modelService.Models.FirstOrDefault(m => m.Id == "gram-q-plus");

        Assert.NotNull(gramQ);
        Assert.Equal("Groq", gramQ.Provider);
        Assert.Contains("GPT OSS 120B", gramQ.Description);

        Assert.NotNull(gramQPlus);
        Assert.Equal("Google", gramQPlus.Provider);
        Assert.Contains("Gemini 3 Flash Preview", gramQPlus.Description);
    }

    [Fact]
    public async Task SearchGroundingHarness_ProducesFormattedMultiSourceContext()
    {
        var provider = new MultiSourceFakeSearchProvider();
        var service = new WebContentService(new MockHttpClientFactory(), searchProviders: [provider]);
        var harness = new SearchGroundingHarness(service, NullLogger<SearchGroundingHarness>.Instance);

        var run = await harness.RunAsync("Latest AI announcements", maxResults: 4, bypassCache: true);

        Assert.True(run.IsGrounded);
        Assert.Equal(4, run.Results.Count);
        Assert.Contains("[1] Title: AI Breakthrough 1", run.Context);
        Assert.Contains("[2] Title: AI Breakthrough 2", run.Context);
        Assert.Contains("[3] Title: AI Breakthrough 3", run.Context);
        Assert.Contains("[4] Title: AI Breakthrough 4", run.Context);
        Assert.Contains("https://news.example/1", run.Context);
    }

    [Fact]
    public void GroundedAnswerValidator_ValidatesMultiSourceCitations()
    {
        var results = new List<WebSearchResult>
        {
            new("OpenAI release", "https://openai.com/blog/model", "OpenAI announced a new model update."),
            new("Google Gemini release", "https://blog.google/technology/ai", "Google released Gemini updates."),
            new("Anthropic Claude news", "https://anthropic.com/news", "Anthropic announced Claude updates.")
        };

        var answer = "According to [OpenAI](https://openai.com/blog/model) and [Google](https://blog.google/technology/ai), new AI models were released.";
        var validation = GroundedAnswerValidator.Validate(answer, results);

        Assert.True(validation.Passed);
        Assert.Equal(2, validation.ValidCitationCount);
    }

    [Fact]
    [Trait("Category", "LiveRun")]
    public async Task LiveRun_NewsOnAppleInTheLastWeek_ReturnsGroundedAnswer()
    {
        var storage = new StorageService();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var modelService = new ModelService(storage);
        var settingsService = new SettingsService(config, storage);
        var gitService = new GitService(storage);
        var toolExecutor = new MockToolExecutor();

        var httpFactory = new RealHttpClientFactory();
        var parallelProvider = new ParallelWebSearchProvider(httpFactory, config, NullLogger<ParallelWebSearchProvider>.Instance);
        var tavilyProvider = new TavilyWebSearchProvider(httpFactory, config, NullLogger<TavilyWebSearchProvider>.Instance);

        var searchProviders = new List<IWebSearchProvider>();
        if (parallelProvider.IsConfigured) searchProviders.Add(parallelProvider);
        if (tavilyProvider.IsConfigured) searchProviders.Add(tavilyProvider);

        var webContentService = new WebContentService(httpFactory, NullLogger<WebContentService>.Instance, searchProviders);
        var searchHarness = new SearchGroundingHarness(webContentService, NullLogger<SearchGroundingHarness>.Instance);

        var orchestrator = new AgentOrchestrator(
            modelService,
            toolExecutor,
            settingsService,
            gitService,
            webContentService,
            searchGroundingHarness: searchHarness);

        string prompt = "What is the news on apple in the last week?";
        string modelId = "gram-q-plus";

        var grounding = await searchHarness.RunAsync(prompt, maxResults: 5, bypassCache: true);
        Assert.True(grounding.IsGrounded);
        Assert.True(grounding.Results.Count > 0);

        // Verify stale years are filtered out of current news
        Assert.DoesNotContain(grounding.Results, r => r.Url.Contains("/2018/") || r.Url.Contains("/2019/"));

        if (modelService.IsConfigured(modelId))
        {
            var answer = new System.Text.StringBuilder();
            var errors = new System.Text.StringBuilder();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            await foreach (var step in orchestrator.RunAgentLoopAsync(
                modelId,
                [new Message("user", prompt)],
                Environment.CurrentDirectory,
                GramSystemPrompt.DefaultBasePrompt,
                maxIterations: 3,
                forcedWebSearchQuery: prompt,
                prefetchedWebContextOverride: grounding.Context,
                disableTools: true,
                cancellationToken: cts.Token))
            {
                if (!string.IsNullOrWhiteSpace(step.ContentDelta))
                {
                    answer.Append(step.ContentDelta);
                }
                if (!string.IsNullOrWhiteSpace(step.ErrorMessage))
                {
                    errors.AppendLine(step.ErrorMessage);
                }
            }

            var answerText = answer.ToString();
            var errorText = errors.ToString();
            Assert.True(string.IsNullOrWhiteSpace(errorText), $"Agent loop failed with error: {errorText}");
            Assert.False(string.IsNullOrWhiteSpace(answerText), "Answer text was empty.");

            var logService = new SearchDiagnosticsLogService(storage, NullLogger<SearchDiagnosticsLogService>.Instance);
            var validation = GroundedAnswerValidator.Validate(answerText, grounding.Results);
            await logService.AppendAsync(new SearchDiagnosticLogEntry
            {
                Query = prompt,
                ModelId = modelId,
                SearchMilliseconds = grounding.ElapsedMilliseconds,
                TotalMilliseconds = grounding.ElapsedMilliseconds,
                ResultCount = grounding.Results.Count,
                GroundingReady = grounding.IsGrounded,
                AnswerUsesSources = true,
                CitationValidationPassed = validation.Passed,
                MaterialClaimCount = validation.MaterialClaimCount,
                CitedMaterialClaimCount = validation.CitedMaterialClaimCount,
                InvalidCitationCount = validation.InvalidCitationCount,
                Answer = answerText,
                Sources = grounding.Results.Select(r => new SearchDiagnosticSourceLog(r.Title, r.Url, r.Snippet, r.Source ?? "Unknown", true)).ToList()
            });
        }
    }

    private sealed class RealHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(25);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            return client;
        }
    }

    private sealed class MockToolExecutor : IToolExecutor
    {
        public List<AgentToolDefinition> GetAvailableTools(bool isLocalEnvironment, GitAccount? activeGitAccount = null, string? intent = null) => [];
        public Task<ToolExecutionResult> ExecuteAsync(string name, string callId, string argumentsJson, string workspacePath, GitAccount? activeGitAccount = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolExecutionResult { ToolName = name, Output = "Mock output", IsError = false });
    }

    private sealed class MultiSourceFakeSearchProvider : IWebSearchProvider
    {
        public string Name => "MultiSourceFake";
        public bool IsConfigured => true;
        public Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WebSearchResult>>([
                new("AI Breakthrough 1", "https://news.example/1", "First AI model released today.", Name),
                new("AI Breakthrough 2", "https://news.example/2", "Second AI research paper published.", Name),
                new("AI Breakthrough 3", "https://news.example/3", "Third AI framework updated.", Name),
                new("AI Breakthrough 4", "https://news.example/4", "Fourth AI benchmark released.", Name)
            ]);
    }

    private sealed class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new MockHandler());
    }

    private sealed class MockHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><head><title>Scraped detail</title></head><body><p>Deep scraped content excerpt.</p></body></html>")
            });
    }
}
