using System.Text.Json.Nodes;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class SubagentEngineTests : IDisposable
{
    private readonly string _tempWorkspace;

    public SubagentEngineTests()
    {
        _tempWorkspace = Path.Combine(Path.GetTempPath(), "gram_subagent_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempWorkspace);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempWorkspace))
            {
                Directory.Delete(_tempWorkspace, true);
            }
        }
        catch {}
    }

    private (ISubagentEngine engine, IAgentToolRegistry toolRegistry) CreateEngine()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        var config = new ConfigurationBuilder().Build();
        var storage = new StorageService();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(storage);
        services.AddSingleton(new SettingsService(config, storage));
        services.AddSingleton(new GitService(storage));
        services.AddSingleton(new AgentSecurityService(storage));
        services.AddSingleton(new ToolboxManagerService(storage));
        services.AddSingleton(new GramBoxWorktreeManager(NullLogger<GramBoxWorktreeManager>.Instance, storage));
        services.AddSingleton(new NativeScratchpadDriver(NullLogger<NativeScratchpadDriver>.Instance));
        services.AddSingleton(new GramBoxService(NullLogger<GramBoxService>.Instance, new GramBoxWorktreeManager(NullLogger<GramBoxWorktreeManager>.Instance, storage), new NativeScratchpadDriver(NullLogger<NativeScratchpadDriver>.Instance)));
        services.AddSingleton<IGramBoxService>(sp => sp.GetRequiredService<GramBoxService>());
        services.AddSingleton<GramToolKernel>();
        services.AddSingleton<WebContentService>();
        services.AddScoped<ModelService>();
        services.AddScoped<IAgentToolRegistry, AgentToolRegistry>();
        services.AddScoped<AgentOrchestrator>();
        services.AddScoped<ISubagentEngine, SubagentEngineService>();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ISubagentEngine>();
        var toolRegistry = scope.ServiceProvider.GetRequiredService<IAgentToolRegistry>();

        return (engine, toolRegistry);
    }

    [Fact]
    public void BuiltinSubagentDefinitions_AreRegisteredCorrectly()
    {
        var (engine, _) = CreateEngine();
        var defs = engine.GetDefinitions();

        Assert.Contains(defs, d => d.Name.Equals("researcher", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(defs, d => d.Name.Equals("coder", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(defs, d => d.Name.Equals("tester", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(defs, d => d.Name.Equals("reviewer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(defs, d => d.Name.Equals("content-worker", StringComparison.OrdinalIgnoreCase));

        var researcher = engine.GetDefinition("researcher");
        Assert.NotNull(researcher);
        Assert.False(researcher.UseGramBoxSandbox);
        Assert.Contains("view_file", researcher.AllowedToolNames);
        Assert.DoesNotContain("write_file", researcher.AllowedToolNames);

        var coder = engine.GetDefinition("coder");
        Assert.NotNull(coder);
        Assert.True(coder.UseGramBoxSandbox);
        Assert.Contains("write_file", coder.AllowedToolNames);
        Assert.Contains("edit_file", coder.AllowedToolNames);

        var contentWorker = engine.GetDefinition("content-worker");
        Assert.NotNull(contentWorker);
        Assert.Contains("visual-design", contentWorker.Capabilities);
        Assert.Contains("writing", contentWorker.Capabilities);
    }

    [Fact]
    public void CapabilityRouting_UsesReusableProfiles_WithoutCreatingTaskSpecificRoles()
    {
        var (engine, toolRegistry) = CreateEngine();

        var visualWorker = engine.ResolveProfile("visual-design");
        var writingWorker = engine.ResolveProfile("writing");
        var presentationTools = toolRegistry.GetAvailableTools(true, intent: "Create a presentation about product strategy.");

        Assert.NotNull(visualWorker);
        Assert.Equal("content-worker", visualWorker.Name);
        Assert.Equal("content-worker", writingWorker?.Name);
        Assert.Contains(presentationTools, tool => tool.Name == "create_presentation_project");
        Assert.Contains(presentationTools, tool => tool.Name == "revise_presentation_section");
        Assert.Contains(presentationTools, tool => tool.Name == "get_presentation_status");
    }

    [Fact]
    public void CustomSubagentDefinition_CanBeRegisteredAndRetrieved()
    {
        var (engine, _) = CreateEngine();
        var custom = new SubagentDefinition
        {
            Name = "security-auditor",
            Role = "Vulnerability Inspector",
            Description = "Audits dependencies and code for CVEs.",
            AllowedToolNames = ["view_file", "search_web"],
            UseGramBoxSandbox = false
        };

        engine.RegisterDefinition(custom);
        var retrieved = engine.GetDefinition("security-auditor");

        Assert.NotNull(retrieved);
        Assert.Equal("security-auditor", retrieved.Name);
        Assert.Equal("Vulnerability Inspector", retrieved.Role);
    }

    [Fact]
    public async Task SpawnSubagent_CreatesAndTracksTask()
    {
        var (engine, _) = CreateEngine();
        var task = await engine.SpawnSubagentAsync(
            parentConversationId: "conv-123",
            subagentName: "researcher",
            role: "API Specialist",
            prompt: "Investigate database connection options.",
            workspacePath: _tempWorkspace,
            useSandbox: false);

        Assert.NotNull(task);
        Assert.NotNull(task.Id);
        Assert.Equal("conv-123", task.ParentConversationId);
        Assert.Equal("researcher", task.SubagentName);
        Assert.Equal("API Specialist", task.Role);

        var retrieved = engine.GetTask(task.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(task.Id, retrieved.Id);

        var tasks = engine.GetTasksForConversation("conv-123");
        Assert.Single(tasks);
        Assert.Equal(task.Id, tasks[0].Id);
    }

    [Fact]
    public async Task ToolRegistry_ExecutesSubagentTools_Correctly()
    {
        var (engine, toolRegistry) = CreateEngine();

        // 1. spawn_subagent tool call
        var spawnArgs = new JsonObject
        {
            ["subagentType"] = "researcher",
            ["role"] = "Auth Reviewer",
            ["taskPrompt"] = "Check JWT validation token claims.",
            ["useSandbox"] = false
        }.ToJsonString();

        var spawnResult = await toolRegistry.ExecuteToolAsync("spawn_subagent", "call-1", spawnArgs, _tempWorkspace);
        Assert.False(spawnResult.IsError);
        Assert.Contains("Subagent Spawned Successfully", spawnResult.Output);

        var tasks = engine.GetTasksForConversation("default-session");
        Assert.NotEmpty(tasks);
        var taskId = tasks[0].Id;

        // 2. get_subagent_status tool call
        var statusArgs = new JsonObject
        {
            ["taskId"] = taskId
        }.ToJsonString();

        var statusResult = await toolRegistry.ExecuteToolAsync("get_subagent_status", "call-2", statusArgs, _tempWorkspace);
        Assert.False(statusResult.IsError);
        Assert.Contains($"[Subagent Task Report: {taskId}]", statusResult.Output);

        // 3. list_subagents tool call
        var listResult = await toolRegistry.ExecuteToolAsync("list_subagents", "call-3", "{}", _tempWorkspace);
        Assert.False(listResult.IsError);
        Assert.Contains("Subagents in Session", listResult.Output);
        Assert.Contains(taskId, listResult.Output);
    }

    [Fact]
    public async Task CancelTask_StopsRunningTask()
    {
        var (engine, _) = CreateEngine();
        var task = await engine.SpawnSubagentAsync(
            parentConversationId: "conv-cancel",
            subagentName: "coder",
            role: "Background Refactorer",
            prompt: "Perform long running refactoring operation.",
            workspacePath: _tempWorkspace,
            useSandbox: false);

        var cancelled = await engine.CancelTaskAsync(task.Id);
        Assert.True(cancelled);
    }
}
