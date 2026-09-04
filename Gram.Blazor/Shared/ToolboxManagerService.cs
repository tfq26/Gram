using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class ToolboxManagerService
{
    private readonly StorageService _storageService;
    private readonly string _toolsFilePath;
    private readonly string _mcpFilePath;
    private readonly string _skillsFilePath;

    public bool IsToolboxInstalled { get; private set; } = true;
    public string ToolboxVersion { get; private set; } = "1.0.0";
    public string ToolboxStatus { get; private set; } = "Connected (Local Engine)";

    public List<ToolboxToolInfo> InstalledTools { get; private set; } = new();
    public List<McpServerConfig> McpServers { get; private set; } = new();
    public List<AgentSkillDefinition> Skills { get; private set; } = new();

    public event Action? OnChange;

    public ToolboxManagerService(StorageService storageService)
    {
        _storageService = storageService;
        _toolsFilePath = _storageService.GetFilePath("toolbox_tools.json");
        _mcpFilePath = _storageService.GetFilePath("mcp_servers.json");
        _skillsFilePath = _storageService.GetFilePath("agent_skills.json");

        InitializeDefaults();
        LoadState();
    }

    private void InitializeDefaults()
    {
        InstalledTools =
        [
            new()
            {
                Id = "embedded-sandbox",
                Name = "Embedded Container Sandbox",
                Category = "Sandbox",
                Description = "Isolated Docker / Podman container runtime for autonomous root command execution, safe package installations, and test runs without host risk.",
                Icon = "",
                Status = "Active"
            },
            new()
            {
                Id = "coder-cde",
                Name = "Coder CDE Integration",
                Category = "Sandbox",
                Description = "Connect to Coder.com enterprise or self-hosted cloud development environments to provision remote agent workspaces.",
                Icon = "",
                Status = "Ready"
            },
            new()
            {
                Id = "tree-sitter-ast",
                Name = "Tree-Sitter Semantic AST",
                Category = "Code",
                Description = "High-precision syntax parser for C#, Rust, TypeScript, Python, and Go. Powers code symbol search and refactoring.",
                Icon = "",
                Status = "Active"
            },
            new()
            {
                Id = "lsp-diagnostics",
                Name = "Language Server (LSP) Bridge",
                Category = "Code",
                Description = "Real-time compiler diagnostics, type inference, and error feedback from Roslyn, rust-analyzer, and vtsls.",
                Icon = "",
                Status = "Active"
            },
            new()
            {
                Id = "ripgrep-fd",
                Name = "Ripgrep & Fd Search Engine",
                Category = "Code",
                Description = "Ultra-fast codebase search engine capable of searching millions of lines of code in milliseconds.",
                Icon = "",
                Status = "Active"
            },
            new()
            {
                Id = "playwright-browser",
                Name = "Playwright Visual QA",
                Category = "Browser",
                Description = "Headless browser for autonomous UI screenshot verification, end-to-end testing, and web rendering checks.",
                Icon = "",
                Status = "Ready"
            },
            new()
            {
                Id = "mcp-manager",
                Name = "Model Context Protocol (MCP)",
                Category = "MCP",
                Description = "Connect to community MCP servers to give models direct access to databases, Figma, Jira, AWS, and custom tools.",
                Icon = "",
                Status = "Active"
            },
            new()
            {
                Id = "git-gram",
                Name = "Git Gram (Autonomous VCS Specialist)",
                Category = "VCS & Git",
                Description = "Specialized Git Gram agent tool for autonomous branch creation, atomic semantic commit generation, interactive conflict resolution, status inspection, and safe staging.",
                Icon = "",
                Status = "Active"
            },
            new()
            {
                Id = "skills-library",
                Name = "Agent Skills & Prompts",
                Category = "Skills",
                Description = "Manage modular domain skills with dedicated system prompts, coding guidelines, and preferred toolchains.",
                Icon = "",
                Status = "Active"
            }
        ];

        McpServers =
        [
            new()
            {
                Id = "mcp-sqlite",
                Name = "SQLite Inspector MCP",
                Transport = "stdio",
                Command = "npx",
                Arguments = "-y @modelcontextprotocol/server-sqlite --db app.db",
                IsEnabled = true,
                IsConnected = true,
                ToolCount = 4
            },
            new()
            {
                Id = "mcp-git",
                Name = "Git Version Control MCP",
                Transport = "stdio",
                Command = "npx",
                Arguments = "-y @modelcontextprotocol/server-git",
                IsEnabled = true,
                IsConnected = true,
                ToolCount = 8
            }
        ];

        Skills =
        [
            new()
            {
                Id = "blazor-architect",
                Name = "Blazor & .NET 9 Specialist",
                Description = "Expert knowledge of Blazor Server/WASM, RenderMode interactive components, SignalR, and modern C# 13 patterns.",
                Icon = "⚡",
                SystemPrompt = "You are a senior Blazor and .NET 9 engineer. Write clean, modular, and reactive C# code following modern .NET conventions.",
                IsActive = true
            },
            new()
            {
                Id = "security-auditor",
                Name = "Security & Vulnerability Auditor",
                Description = "Analyzes source code for injection vulnerabilities, token leaks, OWASP Top 10 risks, and unsafe path traversals.",
                Icon = "🛡️",
                SystemPrompt = "You are an expert application security auditor. Rigorously inspect code for security vulnerabilities, path traversal risks, and sanitization.",
                IsActive = true
            }
        ];
    }

    public void LoadState()
    {
        try
        {
            if (File.Exists(_toolsFilePath))
            {
                var json = File.ReadAllText(_toolsFilePath);
                var loaded = JsonSerializer.Deserialize<List<ToolboxToolInfo>>(json);
                if (loaded != null && loaded.Count > 0) InstalledTools = loaded;
            }
            if (File.Exists(_mcpFilePath))
            {
                var json = File.ReadAllText(_mcpFilePath);
                var loaded = JsonSerializer.Deserialize<List<McpServerConfig>>(json);
                if (loaded != null && loaded.Count > 0) McpServers = loaded;
            }
            if (File.Exists(_skillsFilePath))
            {
                var json = File.ReadAllText(_skillsFilePath);
                var loaded = JsonSerializer.Deserialize<List<AgentSkillDefinition>>(json);
                if (loaded != null && loaded.Count > 0) Skills = loaded;
            }
        }
        catch {}
    }

    public void SaveState()
    {
        try
        {
            File.WriteAllText(_toolsFilePath, JsonSerializer.Serialize(InstalledTools, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(_mcpFilePath, JsonSerializer.Serialize(McpServers, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(_skillsFilePath, JsonSerializer.Serialize(Skills, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch {}
        OnChange?.Invoke();
    }

    public void ToggleTool(string toolId)
    {
        var tool = InstalledTools.FirstOrDefault(t => t.Id == toolId);
        if (tool != null)
        {
            tool.IsEnabled = !tool.IsEnabled;
            tool.Status = tool.IsEnabled ? "Active" : "Disabled";
            SaveState();
        }
    }

    public void AddMcpServer(McpServerConfig config)
    {
        McpServers.Add(config);
        SaveState();
    }

    public void RemoveMcpServer(string serverId)
    {
        McpServers.RemoveAll(s => s.Id == serverId);
        SaveState();
    }

    public void AddSkill(AgentSkillDefinition skill)
    {
        Skills.Add(skill);
        SaveState();
    }

    public void UpdateSkill(AgentSkillDefinition updated)
    {
        var idx = Skills.FindIndex(s => s.Id == updated.Id);
        if (idx >= 0)
        {
            Skills[idx] = updated;
            SaveState();
        }
    }

    public void ToggleSkill(string skillId)
    {
        var skill = Skills.FirstOrDefault(s => s.Id == skillId);
        if (skill != null)
        {
            skill.IsActive = !skill.IsActive;
            SaveState();
        }
    }

    public void RemoveSkill(string skillId)
    {
        Skills.RemoveAll(s => s.Id == skillId);
        SaveState();
    }
}
