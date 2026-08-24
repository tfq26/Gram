using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public enum ExecutionPermissionMode
{
    /// <summary>
    /// Ask user confirmation before executing any command or editing any file.
    /// </summary>
    AskAlways,

    /// <summary>
    /// Automatically approve file modifications and safe commands (build, test, git status),
    /// but ask confirmation for high-risk commands (e.g. rm -rf, drop, disk, install).
    /// </summary>
    SafeAutoApprove,

    /// <summary>
    /// Full autonomy: executes all terminal commands and file edits automatically without prompting,
    /// guarded by the Security Blocklist and Workspace Sandboxing.
    /// </summary>
    FullAutonomous
}

public sealed class SecurityGuardSettings
{
    public ExecutionPermissionMode PermissionMode { get; set; } = ExecutionPermissionMode.FullAutonomous;
    public bool EnforceWorkspaceSandboxing { get; set; } = true;
    public bool BlockDestructiveCommands { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Explicit high-risk or catastrophic command patterns that are always blocked.
    /// </summary>
    public List<string> BlockedPatterns { get; set; } =
    [
        "format ",
        "mkfs",
        "dd if=",
        "rm -rf /",
        "rm -rf ~",
        "rmdir /s /q c:\\",
        "rmdir /s /q c:\\windows",
        ":(){ :|:& };:",
        "shutdown ",
        "reboot",
        "del /f /s /q c:\\",
        "> /dev/sda",
        "> /dev/nvme",
        "curl | sh",
        "wget | sh",
        "curl | bash",
        "wget | bash"
    ];

    public List<string> SafeCommandPrefixes { get; set; } =
    [
        "dotnet build",
        "dotnet test",
        "dotnet run",
        "dotnet restore",
        "npm run",
        "npm test",
        "npm build",
        "pnpm build",
        "cargo check",
        "cargo build",
        "cargo test",
        "git status",
        "git diff",
        "git log",
        "git branch",
        "git show",
        "dir",
        "ls",
        "echo",
        "pwd"
    ];
}

public sealed class PermissionCheckResult
{
    public bool IsAllowed { get; set; } = true;
    public bool RequiresUserConfirmation { get; set; }
    public string? Reason { get; set; }
    public bool IsBlockedBySecurityPolicy { get; set; }
}

public sealed class AgentSecurityService
{
    private readonly StorageService _storageService;
    private readonly string _settingsFilePath;

    public SecurityGuardSettings Settings { get; private set; } = new();
    public event Action? OnChange;

    public AgentSecurityService(StorageService storageService)
    {
        _storageService = storageService;
        _settingsFilePath = _storageService.GetFilePath("security_settings.json");
        LoadSettings();
    }

    public void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = System.Text.Json.JsonSerializer.Deserialize<SecurityGuardSettings>(json);
                if (loaded != null)
                {
                    Settings = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AgentSecurityService] Error loading security settings: {ex.Message}");
        }
    }

    public void SaveSettings()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(Settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
            OnChange?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AgentSecurityService] Error saving security settings: {ex.Message}");
        }
    }

    public void UpdatePermissionMode(ExecutionPermissionMode mode)
    {
        Settings.PermissionMode = mode;
        SaveSettings();
    }

    public PermissionCheckResult EvaluateCommand(string commandLine, string targetDirectory, string workspaceRoot)
    {
        var result = new PermissionCheckResult();
        var trimmedCmd = (commandLine ?? "").Trim();
        var lowerCmd = trimmedCmd.ToLowerInvariant();

        // 1. Check Destructive Hard-Blocklist
        if (Settings.BlockDestructiveCommands)
        {
            foreach (var blocked in Settings.BlockedPatterns)
            {
                if (lowerCmd.Contains(blocked.ToLowerInvariant()))
                {
                    return new PermissionCheckResult
                    {
                        IsAllowed = false,
                        IsBlockedBySecurityPolicy = true,
                        Reason = $"Execution blocked by Security Policy: '{blocked}' is flagged as a catastrophic system command."
                    };
                }
            }
        }

        // 2. Check Directory Sandbox Escape
        if (Settings.EnforceWorkspaceSandboxing && !string.IsNullOrWhiteSpace(workspaceRoot))
        {
            try
            {
                var fullTarget = Path.GetFullPath(targetDirectory);
                var fullRoot = Path.GetFullPath(workspaceRoot);
                if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return new PermissionCheckResult
                    {
                        IsAllowed = false,
                        IsBlockedBySecurityPolicy = true,
                        Reason = $"Execution blocked: Target directory '{targetDirectory}' is outside the authorized workspace root '{workspaceRoot}'."
                    };
                }
            }
            catch {}
        }

        // 3. Evaluate by Permission Mode
        switch (Settings.PermissionMode)
        {
            case ExecutionPermissionMode.FullAutonomous:
                result.IsAllowed = true;
                result.RequiresUserConfirmation = false;
                break;

            case ExecutionPermissionMode.SafeAutoApprove:
                var isSafe = Settings.SafeCommandPrefixes.Any(p => lowerCmd.StartsWith(p.ToLowerInvariant()));
                if (isSafe)
                {
                    result.IsAllowed = true;
                    result.RequiresUserConfirmation = false;
                }
                else
                {
                    result.IsAllowed = true;
                    result.RequiresUserConfirmation = true;
                    result.Reason = $"Command '{trimmedCmd}' requires confirmation (Safe Auto-Approve mode).";
                }
                break;

            case ExecutionPermissionMode.AskAlways:
                result.IsAllowed = true;
                result.RequiresUserConfirmation = true;
                result.Reason = $"Command requires approval before execution.";
                break;
        }

        return result;
    }

    public PermissionCheckResult EvaluateFileAccess(string filePath, string workspaceRoot, bool isWrite)
    {
        if (Settings.EnforceWorkspaceSandboxing && !string.IsNullOrWhiteSpace(workspaceRoot))
        {
            try
            {
                var fullTarget = Path.GetFullPath(Path.Combine(workspaceRoot, filePath));
                var fullRoot = Path.GetFullPath(workspaceRoot);
                if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return new PermissionCheckResult
                    {
                        IsAllowed = false,
                        IsBlockedBySecurityPolicy = true,
                        Reason = $"Path traversal blocked: '{filePath}' escapes workspace root."
                    };
                }
            }
            catch (Exception ex)
            {
                return new PermissionCheckResult
                {
                    IsAllowed = false,
                    IsBlockedBySecurityPolicy = true,
                    Reason = $"Invalid path: {ex.Message}"
                };
            }
        }

        return new PermissionCheckResult { IsAllowed = true };
    }
}
