using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gram.Blazor.Shared;

public sealed class AgentToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public JsonObject Parameters { get; set; } = new();

    public JsonObject ToOpenAiFunctionSchema()
    {
        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = Name,
                ["description"] = Description,
                // JsonNode instances can only have one parent. Tool definitions are reused
                // across agent-loop iterations, so clone the schema before attaching it to
                // the request payload.
                ["parameters"] = Parameters.DeepClone()
            }
        };
    }
}

public sealed class ToolExecutionResult
{
    public string ToolName { get; set; } = "";
    public string CallId { get; set; } = "";
    public string ArgumentsJson { get; set; } = "";
    public string Output { get; set; } = "";
    public bool IsError { get; set; }
    public long DurationMs { get; set; }
}

public interface IAgentToolRegistry
{
    List<AgentToolDefinition> GetAvailableTools(bool isLocalEnvironment, GitAccount? activeGitAccount = null, string? intent = null);
    Task<ToolExecutionResult> ExecuteToolAsync(string name, string callId, string argumentsJson, string workspacePath, GitAccount? activeGitAccount = null, CancellationToken cancellationToken = default);
}

public sealed class AgentToolRegistry : IAgentToolRegistry
{
    private readonly GitService _gitService;
    private readonly AgentSecurityService _securityService;
    private readonly ToolboxManagerService _toolboxService;
    private readonly GramToolKernel _toolKernel;
    private readonly IGramBoxService? _gramBoxService;
    private readonly ISubagentEngine? _subagentEngine;
    private readonly WebContentService _webContentService;
    private readonly PresentationGenerationService? _presentationService;
    private readonly ITaskAndMemoryService? _taskAndMemoryService;
    private readonly HttpClient _httpClient;
    private readonly ICapabilityIntentRouter? _capabilityRouter;

    public AgentToolRegistry(
        GitService gitService, 
        AgentSecurityService securityService,
        ToolboxManagerService toolboxService,
        GramToolKernel toolKernel,
        WebContentService webContentService,
        IGramBoxService? gramBoxService = null,
        ISubagentEngine? subagentEngine = null,
        PresentationGenerationService? presentationService = null,
        ITaskAndMemoryService? taskAndMemoryService = null,
        HttpClient? httpClient = null,
        ICapabilityIntentRouter? capabilityRouter = null)
    {
        _gitService = gitService;
        _securityService = securityService;
        _toolboxService = toolboxService;
        _toolKernel = toolKernel;
        _webContentService = webContentService;
        _presentationService = presentationService;
        _gramBoxService = gramBoxService;
        _subagentEngine = subagentEngine;
        _taskAndMemoryService = taskAndMemoryService;
        _httpClient = httpClient ?? new HttpClient();
        _capabilityRouter = capabilityRouter;
    }

    public List<AgentToolDefinition> GetAvailableTools(bool isLocalEnvironment, GitAccount? activeGitAccount = null, string? intent = null)
    {
        var tools = new List<AgentToolDefinition>();

        if (isLocalEnvironment)
        {
            // 1. view_file
            tools.Add(new AgentToolDefinition
            {
                Name = "view_file",
                Description = "Read the contents of a file from the local workspace filesystem with optional line slicing.",
                Parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["filePath"] = new JsonObject { ["type"] = "string", ["description"] = "Relative path to the file within the workspace." },
                        ["startLine"] = new JsonObject { ["type"] = "integer", ["description"] = "Optional 1-indexed start line to read from." },
                        ["endLine"] = new JsonObject { ["type"] = "integer", ["description"] = "Optional 1-indexed end line (inclusive) to read up to." }
                    },
                    ["required"] = new JsonArray { "filePath" }
                }
            });

            // 2. write_file
            tools.Add(new AgentToolDefinition
            {
                Name = "write_file",
                Description = "Create or overwrite a file with full content in the local workspace.",
                Parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["filePath"] = new JsonObject { ["type"] = "string", ["description"] = "Relative path of the target file to create or overwrite." },
                        ["content"] = new JsonObject { ["type"] = "string", ["description"] = "The full text content to write." }
                    },
                    ["required"] = new JsonArray { "filePath", "content" }
                }
            });

            // 3. edit_file
            tools.Add(new AgentToolDefinition
            {
                Name = "edit_file",
                Description = "Perform surgical replacement of a specific target string or chunk in a file.",
                Parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["filePath"] = new JsonObject { ["type"] = "string", ["description"] = "Relative path of the file to modify." },
                        ["targetContent"] = new JsonObject { ["type"] = "string", ["description"] = "The exact snippet in the file to be replaced." },
                        ["replacementContent"] = new JsonObject { ["type"] = "string", ["description"] = "The new replacement snippet to insert." }
                    },
                    ["required"] = new JsonArray { "filePath", "targetContent", "replacementContent" }
                }
            });

            // 4. list_directory
            tools.Add(new AgentToolDefinition
            {
                Name = "list_directory",
                Description = "List files, subdirectories, and sizes inside a directory path.",
                Parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["directoryPath"] = new JsonObject { ["type"] = "string", ["description"] = "Relative path to list (leave empty for workspace root)." }
                    }
                }
            });

            // 5. find_files
            tools.Add(new AgentToolDefinition
            {
                Name = "find_files",
                Description = "Search for files by glob or pattern (e.g. *.cs, *.razor, *auth*).",
                Parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["pattern"] = new JsonObject { ["type"] = "string", ["description"] = "Search pattern or glob (e.g. *.razor, *Service*)." },
                        ["directoryPath"] = new JsonObject { ["type"] = "string", ["description"] = "Optional relative directory to search within." }
                    },
                    ["required"] = new JsonArray { "pattern" }
                }
            });

            // 6. grep_search
            tools.Add(new AgentToolDefinition
            {
                Name = "grep_search",
                Description = "Search for exact text or regex matches across files in the workspace.",
                Parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Text or pattern to search for across files." },
                        ["extension"] = new JsonObject { ["type"] = "string", ["description"] = "Optional extension filter (e.g. cs, razor, json)." }
                    },
                    ["required"] = new JsonArray { "query" }
                }
            });

            // 7. run_command
            tools.Add(new AgentToolDefinition
            {
                Name = "run_command",
                Description = "Execute a shell or PowerShell command (e.g. dotnet build, dotnet test, git status).",
                Parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["command"] = new JsonObject { ["type"] = "string", ["description"] = "The terminal command line string to run." },
                        ["workingDirectory"] = new JsonObject { ["type"] = "string", ["description"] = "Optional relative working directory for the command." }
                    },
                    ["required"] = new JsonArray { "command" }
                }
            });
        }

        // Git Tools (Both Local and Cloud Remote)
        if (activeGitAccount != null || _gitService.Accounts.Count > 0)
        {
            tools.Add(new AgentToolDefinition
            {
                Name = "git_list_repos",
                Description = "List connected Git repositories available for the current account.",
                Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
            });

            tools.Add(new AgentToolDefinition
            {
                Name = "git_read_file",
                Description = "Fetch file content directly from a remote Git repository (GitHub, GitLab, Forgejo, Bitbucket).",
                Parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["repo"] = new JsonObject { ["type"] = "string", ["description"] = "Full repository name (e.g. owner/repo)." },
                        ["branch"] = new JsonObject { ["type"] = "string", ["description"] = "Branch name (e.g. main, master)." },
                        ["filePath"] = new JsonObject { ["type"] = "string", ["description"] = "Relative path of file in repository." }
                    },
                    ["required"] = new JsonArray { "repo", "filePath" }
                }
            });

            tools.Add(new AgentToolDefinition
            {
                Name = "git_commit_file",
                Description = "Commit a file update or creation directly to a remote Git branch.",
                Parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["repo"] = new JsonObject { ["type"] = "string", ["description"] = "Full repository name (e.g. owner/repo)." },
                        ["branch"] = new JsonObject { ["type"] = "string", ["description"] = "Target branch name." },
                        ["filePath"] = new JsonObject { ["type"] = "string", ["description"] = "Relative file path." },
                        ["content"] = new JsonObject { ["type"] = "string", ["description"] = "Updated file content." },
                        ["commitMessage"] = new JsonObject { ["type"] = "string", ["description"] = "Commit message describing the change." }
                    },
                    ["required"] = new JsonArray { "repo", "branch", "filePath", "content", "commitMessage" }
                }
            });

            tools.Add(new AgentToolDefinition
            {
                Name = "git_create_pr",
                Description = "Open a Pull Request / Merge Request on GitHub, GitLab, Forgejo, or Bitbucket.",
                Parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["repo"] = new JsonObject { ["type"] = "string", ["description"] = "Full repository name (e.g. owner/repo)." },
                        ["title"] = new JsonObject { ["type"] = "string", ["description"] = "PR / MR title." },
                        ["headBranch"] = new JsonObject { ["type"] = "string", ["description"] = "Source feature branch." },
                        ["baseBranch"] = new JsonObject { ["type"] = "string", ["description"] = "Target base branch (default 'main')." },
                        ["description"] = new JsonObject { ["type"] = "string", ["description"] = "Detailed description of the pull request." }
                    },
                    ["required"] = new JsonArray { "repo", "title", "headBranch" }
                }
            });
        }

        // Web Search & Web Fetch Tools (Available Everywhere)
        tools.Add(new AgentToolDefinition
        {
            Name = "search_web",
            Description = "Search the live web for technical documentation, library APIs, tutorials, error solutions, and latest news.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject { ["type"] = "string", ["description"] = "The search terms or question to look up online." }
                },
                ["required"] = new JsonArray { "query" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "resolve_capability",
            Description = "Determine whether an existing Gram capability or connector can satisfy a request before generating a new connector.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["request"] = new JsonObject { ["type"] = "string", ["description"] = "The user's requested operation." } },
                ["required"] = new JsonArray { "request" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "fetch_web_content",
            Description = "Fetch documentation or public web page content via HTTP GET.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["url"] = new JsonObject { ["type"] = "string", ["description"] = "Full HTTP/HTTPS URL to fetch." }
                },
                ["required"] = new JsonArray { "url" }
            }
        });

        // Toolbox Engine & Sandbox Tools (Coder, Container Sandbox, Tree-Sitter AST, LSP, MCP)
        if (_toolboxService.IsToolboxInstalled)
        {
            tools.Add(new AgentToolDefinition
            {
                Name = "toolbox_list_tools",
                Description = "List all active tools, sandbox runtimes (Docker/Podman), Coder CDE bridges, LSP language servers, and MCP connectors in the Gram Toolbox.",
                Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
            });

            tools.Add(new AgentToolDefinition
            {
                Name = "toolbox_execute_tool",
                Description = "Execute an external toolbox service or sandbox container action (e.g. run isolated test container, query tree-sitter AST syntax, query LSP diagnostics).",
                Parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["toolId"] = new JsonObject { ["type"] = "string", ["description"] = "ID of the toolbox tool (e.g. 'embedded-sandbox', 'coder-cde', 'tree-sitter-ast', 'lsp-diagnostics', 'playwright-browser', 'ripgrep-fd')." },
                        ["action"] = new JsonObject { ["type"] = "string", ["description"] = "Action to execute (e.g. 'run', 'inspect', 'diagnose', 'query', 'check_health')." },
                        ["payload"] = new JsonObject { ["type"] = "string", ["description"] = "JSON or text payload parameters for the action." }
                    },
                    ["required"] = new JsonArray { "toolId", "action" }
                }
            });
        }

        // Gram Box Multi-Worktree Parallel Sandbox Tools (Zero-Interruption Agent Autonomy)
        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_create_sandbox",
            Description = "Provision an isolated Git Worktree sandbox environment linked to the current repository for parallel feature development, experimentation, or test runs without touching the main workspace.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["taskName"] = new JsonObject { ["type"] = "string", ["description"] = "Short name or description of the task (e.g. 'Auth Refactor', 'Unit Test Suite')." },
                    ["branchName"] = new JsonObject { ["type"] = "string", ["description"] = "Optional custom branch name for the worktree (e.g. 'feat/auth-v2')." }
                },
                ["required"] = new JsonArray { "taskName" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_exec",
            Description = "Execute shell commands inside an isolated Git Worktree sandbox with 100% agent autonomy (0 approval prompts, low CPU priority nice +10, 0MB idle RAM).",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["command"] = new JsonObject { ["type"] = "string", ["description"] = "The shell command to execute inside the sandbox worktree." },
                    ["sandboxId"] = new JsonObject { ["type"] = "string", ["description"] = "Optional ID of the target worktree sandbox (defaults to active sandbox)." },
                    ["relativeSubDir"] = new JsonObject { ["type"] = "string", ["description"] = "Optional relative subfolder within the worktree." },
                    ["timeoutSeconds"] = new JsonObject { ["type"] = "integer", ["description"] = "Optional timeout in seconds (default 120)." }
                },
                ["required"] = new JsonArray { "command" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_list_sandboxes",
            Description = "List all active parallel Git Worktree sandboxes, branch names, execution statuses, and disk metrics.",
            Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_write_file",
            Description = "Write or edit a file directly in the specified sandbox worktree.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["filePath"] = new JsonObject { ["type"] = "string", ["description"] = "Relative path of file inside the worktree sandbox." },
                    ["content"] = new JsonObject { ["type"] = "string", ["description"] = "The file text content to write." },
                    ["sandboxId"] = new JsonObject { ["type"] = "string", ["description"] = "Optional ID of the target worktree sandbox." }
                },
                ["required"] = new JsonArray { "filePath", "content" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_read_file",
            Description = "Read a file from the specified sandbox worktree.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["filePath"] = new JsonObject { ["type"] = "string", ["description"] = "Relative path of file inside the worktree sandbox." },
                    ["sandboxId"] = new JsonObject { ["type"] = "string", ["description"] = "Optional ID of the target worktree sandbox." }
                },
                ["required"] = new JsonArray { "filePath" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_get_diff",
            Description = "Extract colorized git diff of all modifications made in a sandbox worktree compared to base branch.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["sandboxId"] = new JsonObject { ["type"] = "string", ["description"] = "Optional ID of the target worktree sandbox." }
                }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_commit_sandbox",
            Description = "Commit all changes in the sandbox worktree with a commit message.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["commitMessage"] = new JsonObject { ["type"] = "string", ["description"] = "Commit message describing the changes." },
                    ["sandboxId"] = new JsonObject { ["type"] = "string", ["description"] = "Optional ID of the target worktree sandbox." }
                },
                ["required"] = new JsonArray { "commitMessage" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_preview_merge",
            Description = "Preview whether a sandbox branch can merge cleanly into a target ref without changing the target workspace.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["sandboxId"] = new JsonObject { ["type"] = "string" },
                    ["targetRef"] = new JsonObject { ["type"] = "string", ["description"] = "Optional target Git ref, default HEAD." }
                },
                ["required"] = new JsonArray { "sandboxId" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_close_sandbox",
            Description = "Clean up and remove an isolated worktree sandbox and delete its ephemeral scratchpad.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["sandboxId"] = new JsonObject { ["type"] = "string", ["description"] = "The ID of the sandbox to remove (e.g. 'sb-abc12345')." }
                },
                ["required"] = new JsonArray { "sandboxId" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_checkpoint",
            Description = "Save a durable checkpoint of the current sandbox diff before a risky or major change.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["reason"] = new JsonObject { ["type"] = "string" }, ["sandboxId"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray { "reason" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_rollback",
            Description = "Reset only the isolated sandbox worktree to a previously created checkpoint.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["checkpointId"] = new JsonObject { ["type"] = "string" }, ["sandboxId"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray { "checkpointId" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "grambox_status",
            Description = "Get the health, active engine, and resource telemetry of the Gram Box sandbox engine.",
            Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // Subagent Engine Tools
        tools.Add(new AgentToolDefinition
        {
            Name = "create_presentation_project",
            Description = "Create a structured presentation project using the Gram Q+ presentation pipeline. Routes independent sections to reusable capability-based content workers and returns a project ID for status, revision, and interactive HTML assembly.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["title"] = new JsonObject { ["type"] = "string", ["description"] = "Presentation title." },
                    ["audience"] = new JsonObject { ["type"] = "string", ["description"] = "Intended audience." },
                    ["communicationJob"] = new JsonObject { ["type"] = "string", ["description"] = "What the audience should understand, decide, or do." },
                    ["targetSlideCount"] = new JsonObject { ["type"] = "integer", ["description"] = "Target slide count (default: 1 for single-slide generation, or up to 12)." },
                    ["sections"] = new JsonObject { ["type"] = "array", ["description"] = "Optional section plan. Each section has title, narrativeJob, ownerCapability, and dependencies." }
                },
                ["required"] = new JsonArray { "title", "audience", "communicationJob" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "revise_presentation_section",
            Description = "Revise only one owned presentation section using user feedback. Other sections and their worker tasks remain unchanged.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["projectId"] = new JsonObject { ["type"] = "string" },
                    ["sectionId"] = new JsonObject { ["type"] = "string" },
                    ["feedback"] = new JsonObject { ["type"] = "string" }
                },
                ["required"] = new JsonArray { "projectId", "sectionId", "feedback" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "get_presentation_status",
            Description = "Get presentation project progress, section ownership, worker status, and whether the project is ready for assembly.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["projectId"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray { "projectId" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "get_presentation_logs",
            Description = "Read the durable presentation-generation lifecycle log, including project, section, worker, validation, and assembly events.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["projectId"] = new JsonObject { ["type"] = "string", ["description"] = "Optional project ID filter." },
                    ["maxEntries"] = new JsonObject { ["type"] = "integer", ["description"] = "Optional number of recent entries, maximum 250." }
                }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "assemble_presentation_html",
            Description = "Assemble a ready presentation project into an interactive HTML presentation artifact.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["projectId"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray { "projectId" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "spawn_subagent",
            Description = "Spawn a specialized background subagent ('researcher', 'coder', 'tester', 'reviewer', or custom) to autonomously execute a specific task with optional isolated Gram Box sandbox.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["subagentType"] = new JsonObject { ["type"] = "string", ["description"] = "The type of subagent to invoke: 'researcher', 'coder', 'tester', 'reviewer', or custom name." },
                    ["role"] = new JsonObject { ["type"] = "string", ["description"] = "A 2-5 word job title/role for the subagent (e.g. 'Stripe Webhook Specialist', 'Code Reviewer')." },
                    ["taskPrompt"] = new JsonObject { ["type"] = "string", ["description"] = "A clear, detailed, and actionable prompt specifying what the subagent must accomplish and report." },
                    ["useSandbox"] = new JsonObject { ["type"] = "boolean", ["description"] = "Optional. Whether to spin up a zero-RAM isolated Gram Box worktree sandbox for this subagent (defaults to true for coder/tester)." }
                },
                ["required"] = new JsonArray { "subagentType", "taskPrompt" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "get_subagent_status",
            Description = "Check the live progress, execution logs, status, and generated diff of a spawned subagent task.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["taskId"] = new JsonObject { ["type"] = "string", ["description"] = "The unique taskId returned by spawn_subagent." }
                },
                ["required"] = new JsonArray { "taskId" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "list_subagents",
            Description = "List all active, queued, and completed subagent tasks spawned in the current session.",
            Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 8. Memory, Tasks & Reminders Tools
        tools.Add(new AgentToolDefinition
        {
            Name = "manage_tasks",
            Description = "Create, list, update the status of, or delete actionable tasks on the user's checklist (both in text and voice).",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["description"] = "Action: 'create', 'list', 'update_status', or 'delete'." },
                    ["title"] = new JsonObject { ["type"] = "string", ["description"] = "Title/summary of the task (for 'create')." },
                    ["description"] = new JsonObject { ["type"] = "string", ["description"] = "Optional detailed notes." },
                    ["status"] = new JsonObject { ["type"] = "string", ["description"] = "'todo', 'in_progress', or 'done' (for 'update_status')." },
                    ["priority"] = new JsonObject { ["type"] = "string", ["description"] = "'low', 'medium', 'high', or 'urgent'." },
                    ["dueDate"] = new JsonObject { ["type"] = "string", ["description"] = "Optional ISO date or deadline string." },
                    ["taskId"] = new JsonObject { ["type"] = "string", ["description"] = "Task ID (for 'update_status' or 'delete')." }
                },
                ["required"] = new JsonArray { "action" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "remember_rule",
            Description = "Permanently memorize a user rule, preference, instruction, or project fact that Gram will retain across all future conversations.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["description"] = "Action: 'add', 'list', or 'delete'." },
                    ["ruleContent"] = new JsonObject { ["type"] = "string", ["description"] = "The rule or fact to remember." },
                    ["category"] = new JsonObject { ["type"] = "string", ["description"] = "'preference', 'instruction', 'project_context', or 'personal'." },
                    ["ruleId"] = new JsonObject { ["type"] = "string", ["description"] = "Rule ID (for 'delete')." }
                },
                ["required"] = new JsonArray { "action" }
            }
        });

        tools.Add(new AgentToolDefinition
        {
            Name = "set_reminder",
            Description = "Schedule a reminder for the user at a specified time or relative delay.",
            Parameters = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["description"] = "Action: 'set', 'list', or 'dismiss'." },
                    ["title"] = new JsonObject { ["type"] = "string", ["description"] = "What to remind the user about." },
                    ["remindAt"] = new JsonObject { ["type"] = "string", ["description"] = "When to remind (e.g. 'in 30 minutes', 'tomorrow 9am', or ISO timestamp)." },
                    ["reminderId"] = new JsonObject { ["type"] = "string", ["description"] = "Reminder ID (for 'dismiss')." }
                },
                ["required"] = new JsonArray { "action" }
            }
        });

        return ToolSelectionPolicy.SelectForIntent(tools, intent);
    }

    public async Task<ToolExecutionResult> ExecuteToolAsync(
        string name,
        string callId,
        string argumentsJson,
        string workspacePath,
        GitAccount? activeGitAccount = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new ToolExecutionResult
        {
            ToolName = name,
            CallId = callId,
            ArgumentsJson = argumentsJson
        };

        try
        {
            var args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var normalizedName = name.Contains(':') ? name.Split(':')[^1].Trim().ToLowerInvariant() : name.Trim().ToLowerInvariant();

            switch (normalizedName)
            {
                case "resolve_capability":
                {
                    var request = args?["request"]?.ToString() ?? "";
                    result.Output = _capabilityRouter == null
                        ? "Capability resolver is unavailable."
                        : JsonSerializer.Serialize(_capabilityRouter.Resolve(request), BackendJson.Options);
                    break;
                }

                case "view_file":
                {
                    var filePath = args?["filePath"]?.ToString() ?? "";
                    var fullPath = Path.GetFullPath(Path.Combine(workspacePath, filePath));
                    if (!File.Exists(fullPath))
                    {
                        result.IsError = true;
                        result.Output = $"File not found: {filePath}";
                        break;
                    }

                    var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
                    int startLine = args?["startLine"]?.GetValue<int>() ?? 1;
                    int endLine = args?["endLine"]?.GetValue<int>() ?? lines.Length;

                    startLine = Math.Max(1, Math.Min(startLine, lines.Length));
                    endLine = Math.Max(startLine, Math.Min(endLine, lines.Length));

                    var sb = new StringBuilder();
                    sb.AppendLine($"File: {filePath} (lines {startLine}-{endLine} of {lines.Length}):");
                    for (int i = startLine - 1; i < endLine; i++)
                    {
                        sb.AppendLine($"{i + 1,4}: {lines[i]}");
                    }
                    result.Output = sb.ToString();
                    break;
                }

                case "write_file":
                {
                    var filePath = args?["filePath"]?.ToString() ?? "";
                    var content = args?["content"]?.ToString() ?? "";

                    var fileCheck = _securityService.EvaluateFileAccess(filePath, workspacePath, isWrite: true);
                    if (!fileCheck.IsAllowed)
                    {
                        result.IsError = true;
                        result.Output = fileCheck.Reason ?? "Access denied by security sandbox.";
                        break;
                    }

                    if (fileCheck.RequiresUserConfirmation)
                    {
                        var decision = await _securityService.RequestApprovalAsync(
                            runId: callId,
                            toolName: "write_file",
                            command: null,
                            filePath: filePath,
                            workingDirectory: workspacePath,
                            reason: fileCheck.Reason ?? $"Writing file '{filePath}' requires user confirmation.",
                            operationType: fileCheck.OperationType,
                            exactTarget: fileCheck.ExactTarget ?? filePath,
                            workspaceRoot: fileCheck.WorkspaceRoot ?? workspacePath,
                            isNetworkAccess: fileCheck.IsNetworkAccess,
                            estimatedImpact: fileCheck.EstimatedImpact ?? $"Writes {content.Length} characters to '{filePath}'.",
                            riskLevel: fileCheck.RiskLevel,
                            ct: cancellationToken);

                        if (!decision.Approved)
                        {
                            result.IsError = true;
                            result.Output = $"File modification for '{filePath}' was denied by user.";
                            break;
                        }
                    }

                    var fullPath = Path.GetFullPath(Path.Combine(workspacePath, filePath));

                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    await File.WriteAllTextAsync(fullPath, content, cancellationToken);
                    result.Output = $"Successfully wrote {content.Length} characters to {filePath}.";
                    break;
                }

                case "edit_file":
                {
                    var filePath = args?["filePath"]?.ToString() ?? "";
                    var targetContent = args?["targetContent"]?.ToString() ?? "";
                    var replacementContent = args?["replacementContent"]?.ToString() ?? "";

                    var fileCheck = _securityService.EvaluateFileAccess(filePath, workspacePath, isWrite: true);
                    if (!fileCheck.IsAllowed)
                    {
                        result.IsError = true;
                        result.Output = fileCheck.Reason ?? "Access denied by security sandbox.";
                        break;
                    }

                    if (fileCheck.RequiresUserConfirmation)
                    {
                        var decision = await _securityService.RequestApprovalAsync(
                            runId: callId,
                            toolName: "edit_file",
                            command: null,
                            filePath: filePath,
                            workingDirectory: workspacePath,
                            reason: fileCheck.Reason ?? $"Editing file '{filePath}' requires user confirmation.",
                            operationType: fileCheck.OperationType,
                            exactTarget: fileCheck.ExactTarget ?? filePath,
                            workspaceRoot: fileCheck.WorkspaceRoot ?? workspacePath,
                            isNetworkAccess: fileCheck.IsNetworkAccess,
                            estimatedImpact: fileCheck.EstimatedImpact ?? $"Replaces content in '{filePath}'.",
                            riskLevel: fileCheck.RiskLevel,
                            ct: cancellationToken);

                        if (!decision.Approved)
                        {
                            result.IsError = true;
                            result.Output = $"File edit for '{filePath}' was denied by user.";
                            break;
                        }
                    }

                    var fullPath = Path.GetFullPath(Path.Combine(workspacePath, filePath));

                    if (!File.Exists(fullPath))
                    {
                        result.IsError = true;
                        result.Output = $"File not found: {filePath}";
                        break;
                    }

                    var existing = await File.ReadAllTextAsync(fullPath, cancellationToken);
                    var updated = existing;

                    if (existing.Contains(targetContent))
                    {
                        updated = existing.Replace(targetContent, replacementContent);
                    }
                    else
                    {
                        // Fallback: Normalize line endings (\r\n vs \n)
                        var normalizedExisting = existing.Replace("\r\n", "\n");
                        var normalizedTarget = targetContent.Replace("\r\n", "\n");
                        var normalizedReplacement = replacementContent.Replace("\r\n", "\n");

                        if (normalizedExisting.Contains(normalizedTarget))
                        {
                            var isCrlf = existing.Contains("\r\n");
                            var replaced = normalizedExisting.Replace(normalizedTarget, normalizedReplacement);
                            updated = isCrlf ? replaced.Replace("\n", "\r\n") : replaced;
                        }
                        else
                        {
                            result.IsError = true;
                            result.Output = $"Target content was not found in {filePath}. Please verify the exact lines before editing.";
                            break;
                        }
                    }

                    await File.WriteAllTextAsync(fullPath, updated, cancellationToken);
                    result.Output = $"Successfully edited {filePath}.";
                    break;
                }

                case "list_directory":
                {
                    var relDir = args?["directoryPath"]?.ToString() ?? "";
                    var fullPath = Path.GetFullPath(Path.Combine(workspacePath, relDir));

                    if (!Directory.Exists(fullPath))
                    {
                        result.IsError = true;
                        result.Output = $"Directory not found: {relDir}";
                        break;
                    }

                    var sb = new StringBuilder();
                    var displayDir = string.IsNullOrWhiteSpace(relDir) ? "." : relDir;
                    sb.AppendLine($"Directory contents of '{displayDir}':");

                    void AppendTree(string currentDir, string indent, int depth)
                    {
                        var di = new DirectoryInfo(currentDir);
                        foreach (var sub in di.GetDirectories().OrderBy(d => d.Name).Take(30))
                        {
                            var name = sub.Name;
                            if (name is ".git" or "bin" or "obj" or "node_modules" or ".vs" or ".idea") continue;
                            sb.AppendLine($"{indent}📁 {name}/");
                            if (depth < 2)
                            {
                                AppendTree(sub.FullName, indent + "  ", depth + 1);
                            }
                        }
                        foreach (var file in di.GetFiles().OrderBy(f => f.Name).Take(50))
                        {
                            sb.AppendLine($"{indent}📄 {file.Name} ({file.Length:N0} bytes)");
                        }
                    }

                    AppendTree(fullPath, "", 1);
                    result.Output = sb.ToString();
                    break;
                }

                case "find_files":
                {
                    var pattern = args?["pattern"]?.ToString() ?? "*";
                    var relDir = args?["directoryPath"]?.ToString() ?? "";
                    var searchRoot = Path.GetFullPath(Path.Combine(workspacePath, relDir));

                    if (!Directory.Exists(searchRoot)) searchRoot = workspacePath;

                    var matched = Directory.GetFiles(searchRoot, pattern, SearchOption.AllDirectories)
                        .Select(p => Path.GetRelativePath(workspacePath, p).Replace('\\', '/'))
                        .Where(p => !p.Contains("/bin/") && !p.Contains("/obj/") && !p.Contains("/.git/") && !p.Contains("/node_modules/"))
                        .Take(50)
                        .ToList();

                    if (matched.Count == 0)
                    {
                        result.Output = $"No files matched pattern '{pattern}'.";
                    }
                    else
                    {
                        result.Output = $"Found {matched.Count} matches for '{pattern}':\n" + string.Join("\n", matched.Select(m => $"  - {m}"));
                    }
                    break;
                }

                case "grep_search":
                {
                    var query = args?["query"]?.ToString() ?? "";
                    var ext = args?["extension"]?.ToString() ?? "";
                    var searchPattern = string.IsNullOrWhiteSpace(ext) ? "*.*" : $"*.{ext.TrimStart('.')}";

                    var files = Directory.GetFiles(workspacePath, searchPattern, SearchOption.AllDirectories)
                        .Where(p => {
                            var norm = p.Replace('\\', '/');
                            return !norm.Contains("/bin/") && !norm.Contains("/obj/") && !norm.Contains("/.git/") && !norm.Contains("/node_modules/");
                        })
                        .Take(250);

                    var matches = new List<string>();
                    foreach (var file in files)
                    {
                        var rel = Path.GetRelativePath(workspacePath, file).Replace('\\', '/');
                        var lines = await File.ReadAllLinesAsync(file, cancellationToken);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                            {
                                matches.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
                                if (matches.Count >= 40) break;
                            }
                        }
                        if (matches.Count >= 40) break;
                    }

                    if (matches.Count == 0)
                    {
                        result.Output = $"No matches found for '{query}'.";
                    }
                    else
                    {
                        result.Output = $"Found {matches.Count} matches for '{query}':\n" + string.Join("\n", matches);
                    }
                    break;
                }

                case "run_command":
                {
                    var cmd = args?["command"]?.ToString() ?? "";
                    var relWorkDir = args?["workingDirectory"]?.ToString() ?? "";
                    var targetDir = Path.GetFullPath(Path.Combine(workspacePath, relWorkDir));

                    // Evaluate Security Policy & Permissions
                    var permCheck = _securityService.EvaluateCommand(cmd, targetDir, workspacePath);
                    if (!permCheck.IsAllowed)
                    {
                        result.IsError = true;
                        result.Output = permCheck.Reason ?? "Command execution denied by security policy.";
                        break;
                    }

                    if (permCheck.RequiresUserConfirmation)
                    {
                        var decision = await _securityService.RequestApprovalAsync(
                            runId: callId,
                            toolName: "run_command",
                            command: cmd,
                            filePath: null,
                            workingDirectory: Directory.Exists(targetDir) ? targetDir : workspacePath,
                            reason: permCheck.Reason ?? $"Executing terminal command '{cmd}' requires user confirmation.",
                            operationType: permCheck.OperationType,
                            exactTarget: permCheck.ExactTarget ?? cmd,
                            workspaceRoot: permCheck.WorkspaceRoot ?? workspacePath,
                            isNetworkAccess: permCheck.IsNetworkAccess,
                            estimatedImpact: permCheck.EstimatedImpact,
                            riskLevel: permCheck.RiskLevel,
                            ct: cancellationToken);

                        if (!decision.Approved)
                        {
                            result.IsError = true;
                            result.Output = $"Command execution was denied by user: {cmd}";
                            break;
                        }
                    }

                    string shell;
                    string shellArgs;

                    if (OperatingSystem.IsWindows())
                    {
                        shell = "powershell.exe";
                        shellArgs = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{cmd.Replace("\"", "\\\"")}\"";
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        shell = File.Exists("/bin/zsh") ? "/bin/zsh" : (File.Exists("/bin/bash") ? "/bin/bash" : "sh");
                        shellArgs = $"-c \"{cmd.Replace("\"", "\\\"")}\"";
                    }
                    else // Linux / BSD / generic Unix
                    {
                        shell = File.Exists("/bin/bash") ? "/bin/bash" : (File.Exists("/bin/sh") ? "/bin/sh" : "sh");
                        shellArgs = $"-c \"{cmd.Replace("\"", "\\\"")}\"";
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = shell,
                        Arguments = shellArgs,
                        WorkingDirectory = Directory.Exists(targetDir) ? targetDir : workspacePath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    _securityService.ScrubEnvironment(psi);

                    using var proc = new Process { StartInfo = psi };
                    var stdout = new StringBuilder();
                    var stderr = new StringBuilder();

                    proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                    proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    var timeoutSecs = Math.Max(_securityService.Settings.CommandTimeoutSeconds, 30);
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSecs));

                    try
                    {
                        await proc.WaitForExitAsync(timeoutCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        try { proc.Kill(true); } catch {}
                        result.IsError = true;
                        result.Output = $"Command timed out after 30 seconds: {cmd}\nPartial output:\n{stdout}\n{stderr}";
                        break;
                    }

                    var exitCode = proc.ExitCode;
                    var fullOut = stdout.ToString().Trim();
                    var fullErr = stderr.ToString().Trim();

                    if (exitCode != 0)
                    {
                        result.IsError = true;
                        result.Output = $"Command failed with exit code {exitCode}:\n{fullErr}\n{fullOut}";
                    }
                    else
                    {
                        result.Output = string.IsNullOrWhiteSpace(fullOut) ? (string.IsNullOrWhiteSpace(fullErr) ? "Command completed successfully with 0 output." : fullErr) : fullOut;
                    }
                    break;
                }

                case "git_list_repos":
                {
                    var account = activeGitAccount ?? _gitService.Accounts.FirstOrDefault(a => a.IsDefault) ?? _gitService.Accounts.FirstOrDefault();
                    if (account == null)
                    {
                        result.IsError = true;
                        result.Output = "No Git account connected. Please configure a Git account in Settings > Git.";
                        break;
                    }

                    var provider = _gitService.GetProvider(account.ProviderType);
                    if (provider == null)
                    {
                        result.IsError = true;
                        result.Output = $"Provider {account.ProviderType} not supported.";
                        break;
                    }

                    var repos = await provider.ListRepositoriesAsync(account, cancellationToken);
                    result.Output = $"Repositories for {account.DisplayName} ({repos.Count} total):\n" +
                                   string.Join("\n", repos.Take(25).Select(r => $"  - {r.FullName} (branch: {r.DefaultBranch}) {(r.IsPrivate ? "[Private]" : "")}"));
                    break;
                }

                case "git_read_file":
                {
                    var repo = args?["repo"]?.ToString() ?? "";
                    var branch = args?["branch"]?.ToString() ?? "main";
                    var filePath = args?["filePath"]?.ToString() ?? "";

                    var account = activeGitAccount ?? _gitService.Accounts.FirstOrDefault(a => a.IsDefault) ?? _gitService.Accounts.FirstOrDefault();
                    if (account == null)
                    {
                        result.IsError = true;
                        result.Output = "No Git account connected.";
                        break;
                    }

                    var provider = _gitService.GetProvider(account.ProviderType);
                    if (provider == null)
                    {
                        result.IsError = true;
                        result.Output = $"Provider {account.ProviderType} not available.";
                        break;
                    }

                    var content = await provider.GetFileContentAsync(account, repo, branch, filePath, cancellationToken);
                    result.Output = $"Remote file {repo}@{branch}:{filePath}:\n\n{content}";
                    break;
                }

                case "git_commit_file":
                {
                    var repo = args?["repo"]?.ToString() ?? "";
                    var branch = args?["branch"]?.ToString() ?? "main";
                    var filePath = args?["filePath"]?.ToString() ?? "";
                    var fileContent = args?["content"]?.ToString() ?? "";
                    var msg = args?["commitMessage"]?.ToString() ?? "Update file";

                    var account = activeGitAccount ?? _gitService.Accounts.FirstOrDefault(a => a.IsDefault) ?? _gitService.Accounts.FirstOrDefault();
                    if (account == null)
                    {
                        result.IsError = true;
                        result.Output = "No Git account connected.";
                        break;
                    }

                    var provider = _gitService.GetProvider(account.ProviderType);
                    if (provider == null)
                    {
                        result.IsError = true;
                        result.Output = $"Provider {account.ProviderType} not available.";
                        break;
                    }

                    var changes = new List<FileChange>
                    {
                        new() { Path = filePath, Content = fileContent, Action = "update" }
                    };

                    var commit = await provider.CommitChangesAsync(account, repo, branch, msg, changes, cancellationToken);
                    result.Output = $"Commit created: {commit.Sha} ({commit.Message}) by {commit.Author}\n{commit.HtmlUrl}";
                    break;
                }

                case "git_create_pr":
                {
                    var repo = args?["repo"]?.ToString() ?? "";
                    var title = args?["title"]?.ToString() ?? "New PR";
                    var head = args?["headBranch"]?.ToString() ?? "";
                    var baseBranch = args?["baseBranch"]?.ToString() ?? "main";
                    var desc = args?["description"]?.ToString() ?? "";

                    var account = activeGitAccount ?? _gitService.Accounts.FirstOrDefault(a => a.IsDefault) ?? _gitService.Accounts.FirstOrDefault();
                    if (account == null)
                    {
                        result.IsError = true;
                        result.Output = "No Git account connected.";
                        break;
                    }

                    var provider = _gitService.GetProvider(account.ProviderType);
                    if (provider == null)
                    {
                        result.IsError = true;
                        result.Output = $"Provider {account.ProviderType} not available.";
                        break;
                    }

                    var pr = await provider.CreatePullRequestAsync(account, repo, title, head, baseBranch, desc, cancellationToken);
                    result.Output = $"Pull Request created #{pr.Number}: {pr.Title}\nStatus: {pr.State}\nURL: {pr.HtmlUrl}";
                    break;
                }

                case "search_web":
                {
                    var query = args?["query"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(query))
                    {
                        result.IsError = true;
                        result.Output = "Empty search query.";
                        break;
                    }

                    try
                    {
                        var searchResults = await _webContentService.SearchWebAsync(query, 5, cancellationToken);
                        if (searchResults.Count == 0)
                        {
                            result.Output = $"No live web search results found for query: \"{query}\"";
                        }
                        else
                        {
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine($"# Live Web Search Results for: \"{query}\"\n");
                            int idx = 1;
                            foreach (var item in searchResults)
                            {
                                sb.AppendLine($"{idx}. **[{item.Title}]({item.Url})**");
                                sb.AppendLine($"   {item.Snippet}");
                                sb.AppendLine();
                                idx++;
                            }
                            result.Output = sb.ToString().TrimEnd();
                        }
                    }
                    catch (Exception ex)
                    {
                        result.IsError = true;
                        result.Output = $"Web search failed: {ex.Message}";
                    }
                    break;
                }

                case "toolbox_list_tools":
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"# Gram Toolbox v{_toolboxService.ToolboxVersion} ({_toolboxService.ToolboxStatus})\n");
                    sb.AppendLine("## Kernel-registered tools:");
                    foreach (var t in _toolKernel.GetTools())
                    {
                        var parallel = t.SupportsParallelExecution ? "parallel-safe" : "serialized";
                        sb.AppendLine($"- **{t.Name}** (`{t.Id}`): {t.Description} [Category: {t.Category} | Kind: {t.Kind} | Risk: {t.Risk} | Isolation: {t.Isolation} | ~{t.ExpectedLatencyMs}ms | {parallel}]");
                        if (t.Capabilities.Count > 0)
                        {
                            sb.AppendLine($"  Capabilities: {string.Join(", ", t.Capabilities)}");
                        }
                    }

                    if (_toolboxService.McpServers.Count > 0)
                    {
                        sb.AppendLine("\n## Connected MCP Servers:");
                        foreach (var m in _toolboxService.McpServers)
                        {
                            sb.AppendLine($"- **{m.Name}** (`{m.Id}`): Tools: {m.ToolCount} | Connected: {m.IsConnected} | Command: `{m.Command} {m.Arguments}`");
                        }
                    }

                    if (_toolboxService.Skills.Count > 0)
                    {
                        sb.AppendLine("\n## Active Agent Skills:");
                        foreach (var s in _toolboxService.Skills)
                        {
                            sb.AppendLine($"- **{s.Name}** (`{s.Id}`): {s.Description}");
                        }
                    }

                    result.Output = sb.ToString();
                    break;
                }

                case "toolbox_execute_tool":
                {
                    var toolId = args?["toolId"]?.ToString() ?? "";
                    var action = args?["action"]?.ToString() ?? "run";
                    var payload = args?["payload"]?.ToString() ?? "";

                    var tool = _toolboxService.InstalledTools.FirstOrDefault(t => t.Id.Equals(toolId, StringComparison.OrdinalIgnoreCase));
                    if (tool == null)
                    {
                        result.IsError = true;
                        result.Output = $"Toolbox tool '{toolId}' is not installed or recognized in Gram Toolbox.";
                        break;
                    }

                    var kernelResult = await _toolKernel.ExecuteAsync(
                        toolId,
                        action,
                        new JsonObject { ["payload"] = payload },
                        new GramToolExecutionContext
                        {
                            WorkspacePath = workspacePath,
                            RepositoryPath = workspacePath,
                            ApprovalGranted = _securityService.Settings.PermissionMode == ExecutionPermissionMode.FullAutonomous
                        },
                        cancellationToken);

                    result.IsError = !kernelResult.IsSuccess;
                    result.Output = $"[Toolbox: Controlled Tool Kernel]\nTool: {tool.Name}\nAction: {action}\nStatus: {kernelResult.Status}\nResult:\n{kernelResult.Output}";
                    break;
                }

                case "fetch_web_content":
                {
                    var url = args?["url"]?.ToString() ?? "";
                    var scrapeResult = await _webContentService.ScrapeUrlAsync(url, cancellationToken);
                    if (!scrapeResult.IsSuccess)
                    {
                        result.IsError = true;
                        result.Output = $"Failed to fetch web content from {url}: {scrapeResult.ErrorMessage}";
                    }
                    else
                    {
                        result.Output = scrapeResult.ContentMarkdown;
                    }
                    break;
                }

                case "grambox_create_sandbox":
                {
                    if (_gramBoxService == null)
                    {
                        result.IsError = true;
                        result.Output = "Gram Box service is not registered.";
                        break;
                    }

                    var taskName = args?["taskName"]?.ToString() ?? "Agent Task";
                    var branchName = args?["branchName"]?.ToString();

                    var session = await _gramBoxService.CreateSandboxAsync(workspacePath, branchName, taskName, cancellationToken);
                    result.Output = $"[Gram Box Worktree Sandbox Created]\nSandbox ID: {session.Id}\nTask Name: {session.Name}\nBranch: {session.BranchName}\nWorktree Path: {session.WorktreePath}\nStatus: {session.Status}\nResource Footprint: 0MB Idle RAM / APFS Copy-on-Write";
                    break;
                }

                case "grambox_exec":
                {
                    if (_gramBoxService == null)
                    {
                        result.IsError = true;
                        result.Output = "Gram Box service is not registered.";
                        break;
                    }

                    var command = args?["command"]?.ToString() ?? "";
                    var sandboxId = args?["sandboxId"]?.ToString();
                    var relativeDir = args?["relativeSubDir"]?.ToString();
                    var timeout = args?["timeoutSeconds"]?.GetValue<int>() ?? 120;

                    if (string.IsNullOrWhiteSpace(command))
                    {
                        result.IsError = true;
                        result.Output = "No command provided for grambox_exec.";
                        break;
                    }

                    // Complete agent autonomy inside the worktree sandbox (nice +10, low CPU, 0MB idle RAM)
                    var execRes = await _gramBoxService.ExecuteAsync(command, sandboxId, relativeDir, timeout, cancellationToken);
                    if (!execRes.Success)
                    {
                        result.IsError = true;
                        result.Output = $"[Gram Box Exit Code: {execRes.ExitCode} | Sandbox: {execRes.SandboxId}]\n{execRes.Stderr}\n{execRes.ErrorMessage}";
                    }
                    else
                    {
                        result.Output = string.IsNullOrWhiteSpace(execRes.Stdout) 
                            ? (string.IsNullOrWhiteSpace(execRes.Stderr) ? $"[Gram Box Sandbox {execRes.SandboxId}] Command executed successfully with no output." : execRes.Stderr)
                            : execRes.Stdout;
                    }
                    break;
                }

                case "grambox_list_sandboxes":
                {
                    if (_gramBoxService == null)
                    {
                        result.IsError = true;
                        result.Output = "Gram Box service is not registered.";
                        break;
                    }

                    var sessions = _gramBoxService.ActiveSessions;
                    if (sessions.Count == 0)
                    {
                        result.Output = "No active worktree sandboxes. Create one with `grambox_create_sandbox`.";
                        break;
                    }

                    var sb = new StringBuilder();
                    sb.AppendLine($"Active Worktree Sandboxes ({sessions.Count}):");
                    foreach (var s in sessions)
                    {
                        sb.AppendLine($"- ID: {s.Id} | Task: {s.Name} | Branch: {s.BranchName} | Status: {s.Status} | Path: {s.WorktreePath}");
                    }
                    result.Output = sb.ToString().TrimEnd();
                    break;
                }

                case "grambox_write_file":
                {
                    if (_gramBoxService == null)
                    {
                        result.IsError = true;
                        result.Output = "Gram Box service is not registered.";
                        break;
                    }

                    var filePath = args?["filePath"]?.ToString() ?? "";
                    var content = args?["content"]?.ToString() ?? "";
                    var sandboxId = args?["sandboxId"]?.ToString();

                    var ok = await _gramBoxService.WriteFileAsync(filePath, content, sandboxId, cancellationToken);
                    if (!ok)
                    {
                        result.IsError = true;
                        result.Output = $"Failed to write file '{filePath}' into sandbox.";
                    }
                    else
                    {
                        result.Output = $"Successfully wrote '{filePath}' in sandbox worktree.";
                    }
                    break;
                }

                case "grambox_read_file":
                {
                    if (_gramBoxService == null)
                    {
                        result.IsError = true;
                        result.Output = "Gram Box service is not registered.";
                        break;
                    }

                    var filePath = args?["filePath"]?.ToString() ?? "";
                    var sandboxId = args?["sandboxId"]?.ToString();
                    var content = await _gramBoxService.ReadFileAsync(filePath, sandboxId, cancellationToken);
                    if (content == null)
                    {
                        result.IsError = true;
                        result.Output = $"File not found or could not be read from sandbox: {filePath}";
                    }
                    else
                    {
                        result.Output = content;
                    }
                    break;
                }

                case "grambox_get_diff":
                {
                    if (_gramBoxService == null)
                    {
                        result.IsError = true;
                        result.Output = "Gram Box service is not registered.";
                        break;
                    }

                    var sandboxId = args?["sandboxId"]?.ToString();
                    var diff = await _gramBoxService.GetDiffAsync(sandboxId, cancellationToken);
                    result.Output = $"[Gram Box Diff | Sandbox: {diff.SandboxId} | Branch: {diff.BranchName}]\nFiles Changed: {diff.FilesChanged} (+{diff.Insertions} / -{diff.Deletions})\n\n{(string.IsNullOrWhiteSpace(diff.DiffContent) ? "No uncommitted changes in worktree." : diff.DiffContent)}";
                    break;
                }

                case "grambox_commit_sandbox":
                {
                    if (_gramBoxService == null)
                    {
                        result.IsError = true;
                        result.Output = "Gram Box service is not registered.";
                        break;
                    }

                    var commitMsg = args?["commitMessage"]?.ToString() ?? "Agent update in sandbox worktree";
                    var sandboxId = args?["sandboxId"]?.ToString();
                    var ok = await _gramBoxService.CommitChangesAsync(commitMsg, sandboxId, cancellationToken);
                    result.Output = ok ? $"Successfully committed changes to sandbox branch: \"{commitMsg}\"" : "Failed to commit changes in sandbox.";
                    break;
                }

                case "grambox_preview_merge":
                {
                    if (_gramBoxService == null)
                    {
                        result.IsError = true;
                        result.Output = "Gram Box service is not registered.";
                        break;
                    }

                    var sandboxId = args?["sandboxId"]?.ToString();
                    var targetRef = args?["targetRef"]?.ToString() ?? "HEAD";
                    var preview = await _gramBoxService.PreviewMergeAsync(sandboxId, targetRef, cancellationToken);
                    result.IsError = !preview.CanMergeCleanly;
                    result.Output = $"[Merge Preview | Sandbox: {preview.SandboxId} | {preview.SourceBranch} → {preview.TargetRef}]\nCan merge cleanly: {preview.CanMergeCleanly}\nChanged files: {preview.ChangedFiles.Count}\n{(preview.CanMergeCleanly ? "No merge conflicts detected." : preview.ConflictOutput)}";
                    break;
                }

                case "grambox_close_sandbox":
                {
                    if (_gramBoxService == null)
                    {
                        result.IsError = true;
                        result.Output = "Gram Box service is not registered.";
                        break;
                    }

                    var sandboxId = args?["sandboxId"]?.ToString() ?? "";
                    var ok = await _gramBoxService.CloseSandboxAsync(sandboxId, force: true, cancellationToken);
                    result.Output = ok ? $"Sandbox worktree '{sandboxId}' successfully removed and cleaned up." : $"Failed to remove sandbox '{sandboxId}'.";
                    break;
                }

                case "grambox_checkpoint":
                {
                    if (_gramBoxService == null) { result.IsError = true; result.Output = "Gram Box service is not registered."; break; }
                    var checkpoint = await _gramBoxService.CreateCheckpointAsync(args?["reason"]?.ToString() ?? "Agent checkpoint", args?["sandboxId"]?.ToString(), cancellationToken);
                    result.Output = checkpoint == null ? "No sandbox was available for checkpointing." : $"Checkpoint created: {checkpoint.Id} ({checkpoint.FilesChanged} changed files)";
                    break;
                }

                case "grambox_rollback":
                {
                    if (_gramBoxService == null) { result.IsError = true; result.Output = "Gram Box service is not registered."; break; }
                    var checkpointId = args?["checkpointId"]?.ToString() ?? "";
                    var ok = await _gramBoxService.RollbackAsync(checkpointId, args?["sandboxId"]?.ToString(), cancellationToken);
                    result.IsError = !ok;
                    result.Output = ok ? $"Sandbox rolled back to checkpoint {checkpointId}." : $"Checkpoint {checkpointId} was not found or rollback failed.";
                    break;
                }

                case "grambox_status":
                {
                    if (_gramBoxService == null)
                    {
                        result.Output = "Gram Box: Service not enabled.";
                        break;
                    }

                    var activeCount = _gramBoxService.ActiveSessions.Count;
                    var current = _gramBoxService.ActiveSession;
                    result.Output = $"[Gram Box Multi-Worktree Engine]\nEngine: {_gramBoxService.ActiveEngine}\nStatus: {_gramBoxService.StatusMessage}\nActive Worktree Sessions: {activeCount}\nCurrent Active Sandbox: {(current != null ? $"{current.Id} ({current.BranchName})" : "None")}\nMemory Footprint: 0MB Idle RAM\nCPU Priority: BelowNormal / nice +10 (Zero Host Lag)";
                    break;
                }

                case "spawn_subagent":
                {
                    if (_subagentEngine == null)
                    {
                        result.IsError = true;
                        result.Output = "Subagent Engine service is not registered.";
                        break;
                    }

                    var subagentType = args?["subagentType"]?.ToString() ?? "coder";
                    var role = args?["role"]?.ToString() ?? subagentType;
                    var prompt = args?["taskPrompt"]?.ToString() ?? "";
                    bool? useSandbox = args?["useSandbox"]?.GetValue<bool>();

                    if (string.IsNullOrWhiteSpace(prompt))
                    {
                        result.IsError = true;
                        result.Output = "Error: 'taskPrompt' is required to spawn a subagent.";
                        break;
                    }

                    var parentConversationId = "default-session";
                    var spawned = await _subagentEngine.SpawnSubagentAsync(
                        parentConversationId,
                        subagentType,
                        role,
                        prompt,
                        workspacePath,
                        useSandbox,
                        cancellationToken: cancellationToken);

                    result.Output = $"[Subagent Spawned Successfully]\nTaskId: {spawned.Id}\nSubagent: {spawned.SubagentName}\nRole: {spawned.Role}\nSandbox: {(spawned.SandboxPath != null ? $"Active ({spawned.SandboxId})" : "Main Workspace")}\nStatus: {spawned.Status}\n\nThe subagent is now running autonomously in the background. Use `get_subagent_status` with taskId '{spawned.Id}' to check results.";
                    break;
                }

                case "create_presentation_project":
                {
                    if (_presentationService == null)
                    {
                        result.IsError = true;
                        result.Output = "Presentation generation service is not registered.";
                        break;
                    }

                    var title = args?["title"]?.ToString()?.Trim() ?? "Untitled presentation";
                    var audience = args?["audience"]?.ToString()?.Trim() ?? "General audience";
                    var communicationJob = args?["communicationJob"]?.ToString()?.Trim()
                        ?? "Explain the subject clearly and help the audience understand what to do next.";
                    var targetSlideCount = args?["targetSlideCount"]?.GetValue<int>() ?? 1;
                    var brief = new PresentationBrief
                    {
                        Title = title,
                        Audience = audience,
                        CommunicationJob = communicationJob,
                        TargetSlideCount = targetSlideCount
                    };

                    if (args?["sections"] is JsonArray sectionNodes)
                    {
                        foreach (var sectionNode in sectionNodes.OfType<JsonObject>())
                        {
                            var sectionBrief = new PresentationSectionBrief
                            {
                                Title = sectionNode["title"]?.ToString() ?? "Supporting idea",
                                NarrativeJob = sectionNode["narrativeJob"]?.ToString() ?? "Advance the presentation narrative.",
                                OwnerCapability = sectionNode["ownerCapability"]?.ToString() ?? "writing"
                            };
                            if (sectionNode["dependencies"] is JsonArray dependencies)
                            {
                                sectionBrief.Dependencies = dependencies.Select(item => item?.ToString() ?? "")
                                    .Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
                            }
                            brief.Sections.Add(sectionBrief);
                        }
                    }

                    var project = await _presentationService.CreatePresentationAsync(
                        brief,
                        "default-session",
                        workspacePath,
                        cancellationToken);
                    result.Output = FormatPresentationStatus(project);
                    break;
                }

                case "revise_presentation_section":
                {
                    if (_presentationService == null)
                    {
                        result.IsError = true;
                        result.Output = "Presentation generation service is not registered.";
                        break;
                    }

                    var projectId = args?["projectId"]?.ToString() ?? "";
                    var sectionId = args?["sectionId"]?.ToString() ?? "";
                    var feedback = args?["feedback"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(sectionId) || string.IsNullOrWhiteSpace(feedback))
                    {
                        result.IsError = true;
                        result.Output = "projectId, sectionId, and feedback are required.";
                        break;
                    }

                    var revisedTask = await _presentationService.ReviseSectionAsync(
                        projectId,
                        sectionId,
                        feedback,
                        workspacePath,
                        cancellationToken);
                    result.Output = revisedTask == null
                        ? $"Presentation project or section not found: {projectId}/{sectionId}"
                        : $"Section revision queued. ProjectId: {projectId}\nSectionId: {sectionId}\nWorkerTaskId: {revisedTask.Id}\nThe other presentation sections were not regenerated.";
                    break;
                }

                case "get_presentation_status":
                {
                    if (_presentationService == null)
                    {
                        result.IsError = true;
                        result.Output = "Presentation generation service is not registered.";
                        break;
                    }

                    var project = _presentationService.GetProject(args?["projectId"]?.ToString() ?? "");
                    result.Output = project == null
                        ? "Presentation project not found."
                        : FormatPresentationStatus(project);
                    break;
                }

                case "get_presentation_logs":
                {
                    if (_presentationService == null)
                    {
                        result.IsError = true;
                        result.Output = "Presentation generation service is not registered.";
                        break;
                    }

                    var projectId = args?["projectId"]?.ToString();
                    var maxEntries = args?["maxEntries"]?.GetValue<int>() ?? 100;
                    result.Output = FormatPresentationLogs(_presentationService.GetLogs(projectId, maxEntries));
                    break;
                }

                case "assemble_presentation_html":
                {
                    if (_presentationService == null)
                    {
                        result.IsError = true;
                        result.Output = "Presentation generation service is not registered.";
                        break;
                    }

                    var projectId = args?["projectId"]?.ToString() ?? "";
                    var path = await _presentationService.AssembleHtmlAsync(projectId, cancellationToken);
                    result.Output = path == null
                        ? $"Presentation project '{projectId}' was not found or is not ready for assembly."
                        : $"Interactive HTML presentation assembled: {path}";
                    break;
                }

                case "get_subagent_status":
                {
                    if (_subagentEngine == null)
                    {
                        result.IsError = true;
                        result.Output = "Subagent Engine service is not registered.";
                        break;
                    }

                    var taskId = args?["taskId"]?.ToString() ?? "";
                    var task = _subagentEngine.GetTask(taskId);
                    if (task == null)
                    {
                        result.IsError = true;
                        result.Output = $"Subagent task '{taskId}' was not found.";
                        break;
                    }

                    var sb = new StringBuilder();
                    sb.AppendLine($"[Subagent Task Report: {task.Id}]");
                    sb.AppendLine($"Subagent: {task.SubagentName} ({task.Role})");
                    sb.AppendLine($"Status: {task.Status}");
                    sb.AppendLine($"Duration: {task.DurationMs}ms (Iterations: {task.IterationsUsed})");
                    if (!string.IsNullOrWhiteSpace(task.SandboxPath))
                    {
                        sb.AppendLine($"Sandbox Path: {task.SandboxPath}");
                    }
                    if (task.ModifiedFiles.Count > 0)
                    {
                        sb.AppendLine($"Modified Files ({task.ModifiedFiles.Count}): {string.Join(", ", task.ModifiedFiles)}");
                    }
                    if (!string.IsNullOrWhiteSpace(task.ResultSummary))
                    {
                        sb.AppendLine($"\n--- Summary Output ---\n{task.ResultSummary}");
                    }
                    if (!string.IsNullOrWhiteSpace(task.WorktreeDiff))
                    {
                        sb.AppendLine($"\n--- Worktree Sandbox Diff ---\n{task.WorktreeDiff}");
                    }
                    if (!string.IsNullOrWhiteSpace(task.ErrorMessage))
                    {
                        sb.AppendLine($"\n--- Error ---\n{task.ErrorMessage}");
                    }

                    result.Output = sb.ToString();
                    break;
                }

                case "list_subagents":
                {
                    if (_subagentEngine == null)
                    {
                        result.IsError = true;
                        result.Output = "Subagent Engine service is not registered.";
                        break;
                    }

                    var parentConversationId = "default-session";
                    var tasks = _subagentEngine.GetTasksForConversation(parentConversationId);
                    if (tasks.Count == 0)
                    {
                        result.Output = "No subagent tasks have been spawned in the current session.";
                        break;
                    }

                    var listSb = new StringBuilder();
                    listSb.AppendLine($"[Subagents in Session ({tasks.Count})]");
                    foreach (var t in tasks)
                    {
                        listSb.AppendLine($"- [{t.Status}] TaskId: {t.Id} | {t.SubagentName} ({t.Role}) | Sandbox: {(t.SandboxId != null ? t.SandboxId : "None")} | Modified: {t.ModifiedFiles.Count} files");
                    }
                    result.Output = listSb.ToString();
                    break;
                }

                case "manage_tasks":
                {
                    if (_taskAndMemoryService == null)
                    {
                        result.IsError = true;
                        result.Output = "Task and Memory service is not available.";
                        break;
                    }
                    var action = args?["action"]?.ToString()?.ToLowerInvariant() ?? "list";
                    if (action == "create")
                    {
                        var title = args?["title"]?.ToString() ?? "";
                        var desc = args?["description"]?.ToString();
                        var priority = args?["priority"]?.ToString() ?? "medium";
                        var dueDateStr = args?["dueDate"]?.ToString();
                        DateTimeOffset? dueDate = DateTimeOffset.TryParse(dueDateStr, out var d) ? d : null;
                        var task = await _taskAndMemoryService.CreateTaskAsync(title, desc, priority, dueDate);
                        result.Output = JsonSerializer.Serialize(new { success = true, message = $"Task created: '{task.Title}'", task });
                    }
                    else if (action == "update_status")
                    {
                        var taskId = args?["taskId"]?.ToString() ?? "";
                        var status = args?["status"]?.ToString() ?? "done";
                        var ok = await _taskAndMemoryService.UpdateTaskStatusAsync(taskId, status);
                        result.Output = JsonSerializer.Serialize(new { success = ok, message = ok ? $"Task {taskId} status updated to {status}" : $"Task {taskId} not found" });
                    }
                    else if (action == "delete")
                    {
                        var taskId = args?["taskId"]?.ToString() ?? "";
                        var ok = await _taskAndMemoryService.DeleteTaskAsync(taskId);
                        result.Output = JsonSerializer.Serialize(new { success = ok, message = ok ? $"Task {taskId} deleted" : $"Task {taskId} not found" });
                    }
                    else // list
                    {
                        var tasks = await _taskAndMemoryService.GetTasksAsync();
                        result.Output = JsonSerializer.Serialize(new { count = tasks.Count, tasks });
                    }
                    break;
                }

                case "remember_rule":
                {
                    if (_taskAndMemoryService == null)
                    {
                        result.IsError = true;
                        result.Output = "Task and Memory service is not available.";
                        break;
                    }
                    var action = args?["action"]?.ToString()?.ToLowerInvariant() ?? "add";
                    if (action == "add")
                    {
                        var content = args?["ruleContent"]?.ToString() ?? "";
                        var cat = args?["category"]?.ToString() ?? "preference";
                        var rule = await _taskAndMemoryService.AddMemoryRuleAsync(content, cat);
                        result.Output = JsonSerializer.Serialize(new { success = true, message = $"Remembered rule: '{rule.RuleContent}'", rule });
                    }
                    else if (action == "delete")
                    {
                        var ruleId = args?["ruleId"]?.ToString() ?? "";
                        var ok = await _taskAndMemoryService.DeleteMemoryRuleAsync(ruleId);
                        result.Output = JsonSerializer.Serialize(new { success = ok, message = ok ? $"Rule {ruleId} deleted" : $"Rule {ruleId} not found" });
                    }
                    else // list
                    {
                        var rules = await _taskAndMemoryService.GetActiveMemoryRulesAsync();
                        result.Output = JsonSerializer.Serialize(new { count = rules.Count, rules });
                    }
                    break;
                }

                case "set_reminder":
                {
                    if (_taskAndMemoryService == null)
                    {
                        result.IsError = true;
                        result.Output = "Task and Memory service is not available.";
                        break;
                    }
                    var action = args?["action"]?.ToString()?.ToLowerInvariant() ?? "set";
                    if (action == "set")
                    {
                        var title = args?["title"]?.ToString() ?? "";
                        var remindAtStr = args?["remindAt"]?.ToString() ?? "";
                        var remindAt = DateTimeOffset.UtcNow.AddMinutes(15);
                        if (DateTimeOffset.TryParse(remindAtStr, out var parsedDate))
                        {
                            remindAt = parsedDate;
                        }
                        else if (remindAtStr.Contains("hour", StringComparison.OrdinalIgnoreCase))
                        {
                            remindAt = DateTimeOffset.UtcNow.AddHours(1);
                        }
                        else if (remindAtStr.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
                        {
                            remindAt = DateTimeOffset.UtcNow.AddDays(1);
                        }
                        var reminder = await _taskAndMemoryService.CreateReminderAsync(title, remindAt);
                        result.Output = JsonSerializer.Serialize(new { success = true, message = $"Reminder set for {reminder.RemindAt:yyyy-MM-dd HH:mm UTC}: '{reminder.Title}'", reminder });
                    }
                    else if (action == "dismiss")
                    {
                        var reminderId = args?["reminderId"]?.ToString() ?? "";
                        var ok = await _taskAndMemoryService.DismissReminderAsync(reminderId);
                        result.Output = JsonSerializer.Serialize(new { success = ok, message = ok ? $"Reminder {reminderId} dismissed" : $"Reminder {reminderId} not found" });
                    }
                    else // list
                    {
                        var rems = await _taskAndMemoryService.GetRemindersAsync();
                        result.Output = JsonSerializer.Serialize(new { count = rems.Count, reminders = rems });
                    }
                    break;
                }

                default:
                    result.IsError = true;
                    result.Output = $"Unknown tool: '{name}'";
                    break;
            }
        }
        catch (Exception ex)
        {
            result.IsError = true;
            result.Output = $"Tool execution error: {ex.Message}";
        }
        finally
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    private static string FormatPresentationStatus(ContentProject project)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[Presentation Project: {project.Id}]");
        builder.AppendLine($"Title: {project.Title}");
        builder.AppendLine($"Status: {project.Status}");
        builder.AppendLine($"Revision: {project.Revision}");
        builder.AppendLine($"Sections: {project.Sections.Count(s => s.Status == ContentSectionStatus.Ready)}/{project.Sections.Count} ready");
        builder.AppendLine($"HTML artifact: {project.FinalArtifactPath ?? "not assembled"}");
        builder.AppendLine($"Licensed visual assets: {project.Assets.Count}");
        foreach (var asset in project.Assets)
        {
            builder.AppendLine($"  - {asset.Label} | {asset.Path} | source: {asset.SourceId ?? "not recorded"}");
        }
        foreach (var section in project.Sections.OrderBy(section => section.Order))
        {
            builder.AppendLine($"- {section.Id} | {section.Status} | v{section.Version} | {section.Title} | worker: {section.OwnerProfileId ?? "pending"} | task: {section.OwnerTaskId ?? "pending"}");
        }
        builder.AppendLine("Interactive HTML assembly is available after section outputs pass validation.");
        return builder.ToString();
    }

    private static string FormatPresentationLogs(IReadOnlyList<PresentationGenerationLogEntry> entries)
    {
        if (entries.Count == 0) return "No presentation-generation log entries were found.";
        var builder = new StringBuilder($"[Presentation Generation Logs ({entries.Count})]\n");
        foreach (var entry in entries)
        {
            builder.Append(entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.Append(" | ").Append(entry.Stage).Append(" | ").Append(entry.Message);
            if (!string.IsNullOrWhiteSpace(entry.ProjectId)) builder.Append(" | project=").Append(entry.ProjectId);
            if (!string.IsNullOrWhiteSpace(entry.SectionId)) builder.Append(" | section=").Append(entry.SectionId);
            if (!string.IsNullOrWhiteSpace(entry.TaskId)) builder.Append(" | task=").Append(entry.TaskId);
            if (!string.IsNullOrWhiteSpace(entry.Exception)) builder.Append("\n  exception: ").Append(entry.Exception);
            builder.AppendLine();
        }
        return builder.ToString();
    }
}
