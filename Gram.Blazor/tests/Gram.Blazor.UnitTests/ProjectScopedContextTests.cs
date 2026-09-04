using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ProjectScopedContextTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageService _storageService;
    private readonly SessionStorageService _sessionStorage;
    private readonly SqliteDatabaseService _db;
    private readonly GramAuthService _authService;
    private readonly SettingsService _settingsService;
    private readonly RepositoryMapService _repoMapService;

    public ProjectScopedContextTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "gram_project_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("GRAM_STORAGE_DIR", _tempDir);

        var config = new ConfigurationBuilder().Build();
        _storageService = new StorageService();
        _sessionStorage = new SessionStorageService(_storageService);
        _db = new SqliteDatabaseService(_storageService);

        var httpClientFactory = new TestHttpClientFactory();
        _authService = new GramAuthService(config, httpClientFactory, _storageService, _db);
        _authService.Settings.Provider = AuthProviderType.None;

        _settingsService = new SettingsService(config, _storageService);
        _repoMapService = new RepositoryMapService();
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
    public async Task ProjectService_CreatesAndLoadsProjectsWithKnowledgeFiles()
    {
        var projectService = new ProjectService(_sessionStorage, _db, _authService);
        await projectService.InitializeAsync();

        var project = await projectService.CreateProjectAsync(
            "Gram Research",
            "Deep research and specs for Gram",
            "Always follow technical guidelines strictly.",
            "#3b82f6",
            "book"
        );

        Assert.NotNull(project);
        Assert.Equal("Gram Research", project.Name);
        Assert.Equal("#3b82f6", project.Color);

        // Upload knowledge file
        var sampleDoc = "# Project Architecture Specification\n\nGram uses Blazor Server and Ollama/Gemini.";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sampleDoc));
        var fileInfo = await projectService.UploadProjectFileAsync(project.Id, "architecture.md", stream, "text/markdown");

        Assert.NotNull(fileInfo);
        Assert.Equal("architecture.md", fileInfo.FileName);

        var text = await projectService.ReadProjectFileTextAsync(project.Id, "architecture.md");
        Assert.Equal(sampleDoc, text);

        // Verify retrieval
        var loaded = projectService.GetProject(project.Id);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Files);
        Assert.Equal("architecture.md", loaded.Files[0].FileName);
    }

    [Fact]
    public async Task ContextBuilder_InjectsProjectContextAndKnowledge()
    {
        var projectService = new ProjectService(_sessionStorage, _db, _authService);
        await projectService.InitializeAsync();

        var project = await projectService.CreateProjectAsync(
            "Finance Engine",
            "Ledger calculation engine",
            "Use strict decimal precision for currency amounts."
        );
        project.StrictContextOnly = true;

        var knowledgeDoc = "Tax rate is 20%. Standard deductions are $500.";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(knowledgeDoc));
        await projectService.UploadProjectFileAsync(project.Id, "tax_rules.txt", stream, "text/plain");

        var contextBuilder = new ContextBuilder(_sessionStorage, _settingsService, _repoMapService, null, projectService);

        var conv = new Conversation("conv-test-1", "Tax chat", DateTimeOffset.UtcNow)
        {
            ProjectId = project.Id
        };
        conv.Messages.Add(new Message("user", "What is the tax rate?"));

        var result = await contextBuilder.AssembleContextAsync(conv);

        Assert.Contains("<project_context", result.SystemPrompt);
        Assert.Contains("Finance Engine", result.SystemPrompt);
        Assert.Contains("<project_instructions>", result.SystemPrompt);
        Assert.Contains("Use strict decimal precision for currency amounts.", result.SystemPrompt);
        Assert.Contains("<project_knowledge_base>", result.SystemPrompt);
        Assert.Contains("Tax rate is 20%. Standard deductions are $500.", result.SystemPrompt);
        Assert.Contains("<strict_context_isolation>", result.SystemPrompt);
    }

    [Fact]
    public async Task ContextBuilder_StrictContext_OmitsRepositoryMap()
    {
        var projectService = new ProjectService(_sessionStorage, _db, _authService);
        await projectService.InitializeAsync();

        var project = await projectService.CreateProjectAsync("Isolated Sandbox");
        project.StrictContextOnly = true;
        await projectService.SaveProjectAsync(project);

        var contextBuilder = new ContextBuilder(_sessionStorage, _settingsService, _repoMapService, null, projectService);

        var conv = new Conversation("conv-test-2", "Isolated chat", DateTimeOffset.UtcNow)
        {
            ProjectId = project.Id
        };

        var result = await contextBuilder.AssembleContextAsync(conv, workspacePath: _tempDir);

        Assert.Null(result.RepositoryMap);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
