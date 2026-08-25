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
    private readonly HttpClient _httpClient;

    public AgentToolRegistry(
        GitService gitService, 
        AgentSecurityService securityService,
        ToolboxManagerService toolboxService,
        GramToolKernel toolKernel,
        IGramBoxService? gramBoxService = null,
        HttpClient? httpClient = null)
    {
        _gitService = gitService;
        _securityService = securityService;
        _toolboxService = toolboxService;
        _toolKernel = toolKernel;
        _gramBoxService = gramBoxService;
        _httpClient = httpClient ?? new HttpClient();
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
            Name = "grambox_status",
            Description = "Get the health, active engine, and resource telemetry of the Gram Box sandbox engine.",
            Parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
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

            switch (name.ToLowerInvariant())
            {
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

                    var fullPath = Path.GetFullPath(Path.Combine(workspacePath, filePath));

                    if (!File.Exists(fullPath))
                    {
                        result.IsError = true;
                        result.Output = $"File not found: {filePath}";
                        break;
                    }

                    var existing = await File.ReadAllTextAsync(fullPath, cancellationToken);
                    if (!existing.Contains(targetContent))
                    {
                        result.IsError = true;
                        result.Output = $"Target content was not found in {filePath}. Please verify the exact lines before editing.";
                        break;
                    }

                    var updated = existing.Replace(targetContent, replacementContent);
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

                    var dirInfo = new DirectoryInfo(fullPath);
                    var sb = new StringBuilder();
                    sb.AppendLine($"Directory contents of '{relDir}':");

                    foreach (var sub in dirInfo.GetDirectories().OrderBy(d => d.Name).Take(50))
                    {
                        sb.AppendLine($"📁 {sub.Name}/");
                    }
                    foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name).Take(100))
                    {
                        sb.AppendLine($"📄 {file.Name} ({file.Length:N0} bytes)");
                    }

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
                        .Where(p => !p.Contains("/bin/") && !p.Contains("/obj/") && !p.Contains("/.git/"))
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
                        .Where(p => !p.Contains("\\bin\\") && !p.Contains("\\obj\\") && !p.Contains("\\.git\\"))
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

                    var isWindows = OperatingSystem.IsWindows();
                    var shell = isWindows ? "powershell.exe" : "/bin/bash";
                    var shellArgs = isWindows ? $"-NoProfile -Command \"{cmd.Replace("\"", "\\\"")}\"" : $"-c \"{cmd.Replace("\"", "\\\"")}\"";

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

                    using var proc = new Process { StartInfo = psi };
                    var stdout = new StringBuilder();
                    var stderr = new StringBuilder();

                    proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                    proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

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
                        var encoded = Uri.EscapeDataString(query);
                        var searchUrl = $"https://html.duckduckgo.com/html/?q={encoded}";
                        using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                        using var res = await _httpClient.SendAsync(req, cancellationToken);
                        if (!res.IsSuccessStatusCode)
                        {
                            result.IsError = true;
                            result.Output = $"Search engine error {(int)res.StatusCode}: {res.ReasonPhrase}";
                            break;
                        }

                        var html = await res.Content.ReadAsStringAsync(cancellationToken);
                        
                        // Parse snippets from DuckDuckGo HTML results
                        var doc = new System.Text.StringBuilder();
                        doc.AppendLine($"# Web Search Results for: \"{query}\"\n");

                        var matches = System.Text.RegularExpressions.Regex.Matches(html, @"<a class=""result__url""[^>]*href=""([^""]+)""[^>]*>.*?</a>.*?<a class=""result__snippet""[^>]*>(.*?)</a>", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        
                        int count = 0;
                        foreach (System.Text.RegularExpressions.Match m in matches)
                        {
                            if (count >= 6) break;
                            var rawUrl = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
                            var snippet = System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(m.Groups[2].Value), "<.*?>", string.Empty).Trim();
                            
                            if (!string.IsNullOrWhiteSpace(snippet))
                            {
                                count++;
                                doc.AppendLine($"{count}. **Link**: {rawUrl}");
                                doc.AppendLine($"   **Snippet**: {snippet}\n");
                            }
                        }

                        if (count == 0)
                        {
                            // Fallback to strip text summary if structured regex didn't match
                            var cleanText = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ");
                            cleanText = System.Text.RegularExpressions.Regex.Replace(cleanText, @"\s+", " ").Trim();
                            doc.AppendLine(cleanText.Length > 2000 ? cleanText[..2000] : cleanText);
                        }

                        result.Output = doc.ToString();
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
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        result.IsError = true;
                        result.Output = $"Invalid URL: {url}";
                        break;
                    }

                    using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                    req.Headers.UserAgent.ParseAdd("Gram-Agent/1.0");

                    using var res = await _httpClient.SendAsync(req, cancellationToken);
                    if (!res.IsSuccessStatusCode)
                    {
                        result.IsError = true;
                        result.Output = $"HTTP Error {(int)res.StatusCode}: {res.ReasonPhrase}";
                        break;
                    }

                    var body = await res.Content.ReadAsStringAsync(cancellationToken);
                    // Squeeze output if excessive
                    if (body.Length > 12000)
                    {
                        body = body[..12000] + "\n\n...[content truncated]...";
                    }
                    result.Output = body;
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
}
