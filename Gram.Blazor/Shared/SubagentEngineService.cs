using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gram.Blazor.Shared;

public interface ISubagentEngine
{
    event Action<SubagentTask>? OnTaskUpdated;
    IReadOnlyList<SubagentDefinition> GetDefinitions();
    SubagentDefinition? GetDefinition(string name);
    SubagentDefinition? ResolveProfile(string capability, string? preferredProfileId = null);
    void RegisterDefinition(SubagentDefinition definition);
    Task<SubagentTask> SpawnTaskAsync(SubagentTaskSpec spec, CancellationToken cancellationToken = default);
    Task<SubagentTask> SpawnSubagentAsync(string parentConversationId, string subagentName, string role, string prompt, string workspacePath, bool? useSandbox = null, string? modelId = null, CancellationToken cancellationToken = default);
    SubagentTask? GetTask(string taskId);
    IReadOnlyList<SubagentTask> GetTasksForConversation(string parentConversationId);
    Task<bool> CancelTaskAsync(string taskId);
}

public sealed class SubagentEngineService : ISubagentEngine
{
    private readonly ConcurrentDictionary<string, SubagentDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SubagentTask> _tasks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _taskCts = new();
    
    private readonly IServiceProvider _serviceProvider;
    private readonly IGramBoxService _gramBoxService;
    private readonly ModelService _modelService;

    public event Action<SubagentTask>? OnTaskUpdated;

    public SubagentEngineService(
        IServiceProvider serviceProvider,
        IGramBoxService gramBoxService,
        ModelService modelService)
    {
        _serviceProvider = serviceProvider;
        _gramBoxService = gramBoxService;
        _modelService = modelService;

        RegisterBuiltinDefinitions();
    }

    private void RegisterBuiltinDefinitions()
    {
        // 1. Researcher (Read-only discovery & web investigation)
        RegisterDefinition(new SubagentDefinition
        {
            Name = "researcher",
            Role = "Codebase & Web Researcher",
            Description = "Explores the workspace codebase and searches the web without modifying files.",
            SystemPrompt = "You are a specialized Research Subagent. Your mission is to thoroughly explore the codebase, search for relevant implementations, search the web for documentation or technical solutions, and provide a clear, detailed analytical summary. You do not make code modifications.",
            Capabilities = ["research", "source-analysis"],
            AllowedToolNames = ["view_file", "list_directory", "find_files", "grep_search", "search_web", "fetch_web_content", "toolbox_execute_tool"],
            UseGramBoxSandbox = false
        });

        // 2. Coder (Isolated implementation worker)
        RegisterDefinition(new SubagentDefinition
        {
            Name = "coder",
            Role = "Sandbox Code Implementation Worker",
            Description = "Implements code features, bug fixes, or refactors inside an isolated Gram Box sandbox.",
            SystemPrompt = "You are a specialized Coder Subagent operating inside an isolated worktree sandbox. Implement the requested code changes cleanly and completely using your file viewing, writing, and editing tools. Focus solely on delivering working code.",
            Capabilities = ["implementation", "coding"],
            AllowedToolNames = ["view_file", "write_file", "edit_file", "list_directory", "find_files", "grep_search", "search_web"],
            UseGramBoxSandbox = true
        });

        // 3. Tester (Test executor and validator)
        RegisterDefinition(new SubagentDefinition
        {
            Name = "tester",
            Role = "Test & Validation Worker",
            Description = "Executes test suites, builds, and diagnostic checks inside a sandbox.",
            SystemPrompt = "You are a specialized Testing & Verification Subagent. Run unit tests, builds, and diagnostic commands to verify functionality. Report any failures with exact error messages and stack traces.",
            Capabilities = ["validation", "quality-assurance"],
            AllowedToolNames = ["view_file", "list_directory", "find_files", "grep_search", "run_command"],
            UseGramBoxSandbox = true
        });

        // 4. Reviewer (Code quality & architectural validator)
        RegisterDefinition(new SubagentDefinition
        {
            Name = "reviewer",
            Role = "Code Review & Quality Auditor",
            Description = "Audits code diffs, security implications, and architectural consistency.",
            SystemPrompt = "You are a specialized Code Review Subagent. Inspect code changes, diffs, and security patterns. Provide actionable, concise feedback on correctness, edge cases, and code quality.",
            Capabilities = ["review", "security", "quality-assurance"],
            AllowedToolNames = ["view_file", "list_directory", "find_files", "grep_search", "search_web"],
            UseGramBoxSandbox = false
        });

        RegisterDefinition(new SubagentDefinition
        {
            Name = "content-worker",
            Role = "Content Generation Specialist",
            Description = "Reusable worker for writing, visual direction, data-backed content, and format-specific section drafts.",
            SystemPrompt = "You are a content-generation specialist. Produce only the requested structured section output, keep claims evidence-led, make the visual idea concrete, and follow the supplied output schema exactly. Do not assemble the whole artifact or rewrite unrelated sections.",
            Capabilities = ["writing", "visual-design", "data-analysis", "content-generation"],
            AllowedToolNames = ["view_file", "list_directory", "find_files", "grep_search", "search_web", "fetch_web_content"],
            UseGramBoxSandbox = false
        });
    }

    public IReadOnlyList<SubagentDefinition> GetDefinitions() => _definitions.Values.ToList();

    public SubagentDefinition? GetDefinition(string name)
    {
        _definitions.TryGetValue(name, out var def);
        return def;
    }

    public SubagentDefinition? ResolveProfile(string capability, string? preferredProfileId = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredProfileId))
        {
            var preferred = GetDefinition(preferredProfileId);
            if (preferred != null) return preferred;
        }

        return _definitions.Values
            .Where(profile => profile.Capabilities.Any(item => item.Equals(capability, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(profile => profile.Name.Equals("content-worker", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault()
            ?? _definitions.Values.FirstOrDefault(profile => profile.Name.Equals("researcher", StringComparison.OrdinalIgnoreCase));
    }

    public void RegisterDefinition(SubagentDefinition definition)
    {
        _definitions[definition.Name] = definition;
    }

    public async Task<SubagentTask> SpawnTaskAsync(SubagentTaskSpec spec, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spec.Prompt)) throw new ArgumentException("Task prompt is required.", nameof(spec));
        var profile = ResolveProfile(spec.Capability, spec.PreferredProfileId)
            ?? throw new InvalidOperationException($"No worker profile supports capability '{spec.Capability}'.");
        var task = await SpawnSubagentAsync(
            spec.ParentConversationId,
            profile.Name,
            spec.RoleOverride ?? profile.Role,
            spec.Prompt,
            spec.WorkspacePath,
            spec.UseSandbox,
            spec.ModelId,
            cancellationToken);

        task.Capability = spec.Capability;
        task.TaskType = spec.TaskType;
        task.SectionId = spec.SectionId;
        task.OutputFormat = spec.OutputFormat;
        task.Dependencies = spec.Dependencies.ToList();
        // The worker can finish very quickly on an unavailable provider. Emit a
        // second update after metadata is attached so section owners never miss
        // the terminal state.
        NotifyTaskUpdated(task);
        return task;
    }

    public async Task<SubagentTask> SpawnSubagentAsync(
        string parentConversationId,
        string subagentName,
        string role,
        string prompt,
        string workspacePath,
        bool? useSandbox = null,
        string? modelId = null,
        CancellationToken cancellationToken = default)
    {
        var def = GetDefinition(subagentName) ?? new SubagentDefinition
        {
            Name = subagentName,
            Role = role,
            Description = "Dynamic subagent",
            SystemPrompt = $"You are a specialized subagent for {role}. Task: {prompt}",
            Capabilities = [role],
            AllowedToolNames = ["view_file", "write_file", "edit_file", "list_directory", "find_files", "grep_search", "search_web"],
            UseGramBoxSandbox = useSandbox ?? false
        };

        var shouldSandbox = useSandbox ?? def.UseGramBoxSandbox;
        var task = new SubagentTask
        {
            ParentConversationId = parentConversationId,
            SubagentName = def.Name,
            Role = string.IsNullOrWhiteSpace(role) ? def.Role : role,
            Prompt = prompt,
            WorkspacePath = workspacePath,
            Status = SubagentTaskStatus.Queued
        };

        if (shouldSandbox && _gramBoxService.IsReady)
        {
            try
            {
                var sandboxName = $"{def.Name}-{task.Id}";
                var session = await _gramBoxService.CreateSandboxAsync(workspacePath, branchName: null, taskName: sandboxName, ct: cancellationToken);
                task.SandboxId = session.Id;
                task.SandboxPath = session.WorktreePath;
            }
            catch (Exception ex)
            {
                task.ExecutionLogs.Add($"[Warning] Failed to initialize Gram Box sandbox: {ex.Message}. Falling back to workspace.");
            }
        }

        _tasks[task.Id] = task;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _taskCts[task.Id] = cts;

        NotifyTaskUpdated(task);

        // Spawn execution in background task
        _ = Task.Run(() => ExecuteSubagentLoopAsync(task, def, modelId, cts.Token));

        return task;
    }

    private async Task ExecuteSubagentLoopAsync(SubagentTask task, SubagentDefinition def, string? modelId, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        task.StartedAt = DateTimeOffset.UtcNow;
        task.Status = SubagentTaskStatus.Running;
        NotifyTaskUpdated(task);

        try
        {
            var targetWorkspace = !string.IsNullOrWhiteSpace(task.SandboxPath) ? task.SandboxPath : task.WorkspacePath;
            var targetModel = !string.IsNullOrWhiteSpace(modelId) ? modelId : (def.ModelId ?? _modelService.Models.FirstOrDefault(m => m.IsFavorite)?.Id ?? _modelService.Models.FirstOrDefault()?.Id ?? "gemini-2.5-flash");

            using var scope = _serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetService(typeof(AgentOrchestrator)) as AgentOrchestrator;

            if (orchestrator == null)
            {
                throw new InvalidOperationException("AgentOrchestrator service is unavailable.");
            }

            var subagentHistory = new List<Message>
            {
                new Message("user", task.Prompt)
            };

            var fullSystemPrompt = new StringBuilder();
            fullSystemPrompt.AppendLine(def.SystemPrompt);
            fullSystemPrompt.AppendLine($"\nYour designated role: {task.Role}");
            if (!string.IsNullOrWhiteSpace(task.SandboxPath))
            {
                fullSystemPrompt.AppendLine($"- Running inside isolated Gram Box sandbox: {task.SandboxPath}");
                fullSystemPrompt.AppendLine("- All file modifications are contained within this isolated worktree.");
            }

            var resultBuilder = new StringBuilder();
            int iterations = 0;

            await foreach (var update in orchestrator.RunAgentLoopAsync(
                targetModel,
                subagentHistory,
                targetWorkspace,
                fullSystemPrompt.ToString(),
                maxIterations: 6,
                allowedToolNames: def.AllowedToolNames,
                cancellationToken: ct))
            {
                if (!string.IsNullOrWhiteSpace(update.ContentDelta))
                {
                    resultBuilder.Append(update.ContentDelta);
                }

                if (update.ToolCall != null)
                {
                    iterations++;
                    task.ExecutionLogs.Add($"Tool Call: {update.ToolCall.ToolName}({update.ToolCall.Arguments})");
                    NotifyTaskUpdated(task);
                }

                if (update.ToolResult != null)
                {
                    if (update.ToolResult.ToolName is "write_file" or "edit_file")
                    {
                        try
                        {
                            var args = JsonNode.Parse(update.ToolResult.ArgumentsJson)?.AsObject();
                            var path = args?["filePath"]?.ToString() ?? args?["targetFile"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(path) && !task.ModifiedFiles.Contains(path))
                            {
                                task.ModifiedFiles.Add(path);
                            }
                        }
                        catch {}
                    }
                }
            }

            task.ResultSummary = resultBuilder.ToString();
            task.IterationsUsed = iterations;

            // If running in a Gram Box sandbox, collect the worktree diff summary
            if (!string.IsNullOrWhiteSpace(task.SandboxId))
            {
                try
                {
                    var diffSummary = await _gramBoxService.GetDiffAsync(task.SandboxId, ct);
                    task.WorktreeDiff = diffSummary.DiffContent;
                }
                catch (Exception ex)
                {
                    task.ExecutionLogs.Add($"[Diff Error] Failed to retrieve worktree diff: {ex.Message}");
                }
            }

            task.Status = SubagentTaskStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            task.Status = SubagentTaskStatus.Cancelled;
            task.ErrorMessage = "Subagent execution was cancelled.";
        }
        catch (Exception ex)
        {
            task.Status = SubagentTaskStatus.Failed;
            task.ErrorMessage = ex.Message;
        }
        finally
        {
            sw.Stop();
            task.DurationMs = sw.ElapsedMilliseconds;
            task.CompletedAt = DateTimeOffset.UtcNow;
            _taskCts.TryRemove(task.Id, out _);
            NotifyTaskUpdated(task);
        }
    }

    public SubagentTask? GetTask(string taskId)
    {
        _tasks.TryGetValue(taskId, out var task);
        return task;
    }

    public IReadOnlyList<SubagentTask> GetTasksForConversation(string parentConversationId)
    {
        return _tasks.Values
            .Where(t => t.ParentConversationId.Equals(parentConversationId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.CreatedAt)
            .ToList();
    }

    public Task<bool> CancelTaskAsync(string taskId)
    {
        if (_taskCts.TryGetValue(taskId, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch {}
            if (_tasks.TryGetValue(taskId, out var task))
            {
                task.Status = SubagentTaskStatus.Cancelled;
                NotifyTaskUpdated(task);
            }
            return Task.FromResult(true);
        }
        if (_tasks.TryGetValue(taskId, out var t))
        {
            t.Status = SubagentTaskStatus.Cancelled;
            NotifyTaskUpdated(t);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    private void NotifyTaskUpdated(SubagentTask task)
    {
        OnTaskUpdated?.Invoke(task);
    }
}
