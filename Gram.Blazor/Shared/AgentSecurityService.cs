using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Gram.Blazor.Shared;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExecutionPermissionMode
{
    /// <summary>
    /// Ask user confirmation before executing any command or editing any file.
    /// </summary>
    AskAlways,

    /// <summary>
    /// Automatically approve safe read-only commands, build/test runs, and in-workspace file modifications,
    /// but ask confirmation for network operations, deletions/destructive commands, and unclassified commands.
    /// </summary>
    SafeAutoApprove,

    /// <summary>
    /// Full autonomy: executes workspace commands and file edits automatically without prompting,
    /// guarded by the Security Blocklist and Workspace Sandboxing.
    /// </summary>
    FullAutonomous
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommandRiskLevel
{
    Low,
    Medium,
    High,
    Critical,
    Blocked
}

public sealed record CommandClassification(
    CommandRiskLevel RiskLevel,
    string OperationType,
    bool IsNetworkAccess,
    bool IsDestructive,
    string EstimatedImpact,
    bool RequiresApproval,
    bool IsHardBlocked,
    string? BlockReason = null);

public sealed class SecurityGuardSettings
{
    public ExecutionPermissionMode PermissionMode { get; set; } = ExecutionPermissionMode.FullAutonomous;
    public bool EnforceWorkspaceSandboxing { get; set; } = true;
    public bool BlockDestructiveCommands { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public bool ScrubChildProcessEnvironment { get; set; } = true;

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
        "rm -rf /*",
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
        "wget | bash",
        "curl | zsh",
        "wget | zsh"
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
        "pnpm test",
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

public static class EnvironmentScrubber
{
    private static readonly HashSet<string> SensitiveExactKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ANTHROPIC_API_KEY",
        "OPENAI_API_KEY",
        "GEMINI_API_KEY",
        "GOOGLE_API_KEY",
        "DEEPSEEK_API_KEY",
        "XAI_API_KEY",
        "GROK_API_KEY",
        "MISTRAL_API_KEY",
        "AZURE_OPENAI_API_KEY",
        "AZURE_API_KEY",
        "COHERE_API_KEY",
        "AWS_ACCESS_KEY_ID",
        "AWS_SECRET_ACCESS_KEY",
        "AWS_SESSION_TOKEN",
        "AWS_SECURITY_TOKEN",
        "GITHUB_TOKEN",
        "GH_TOKEN",
        "GITLAB_TOKEN",
        "CODEBERG_TOKEN",
        "BITBUCKET_TOKEN",
        "CODER_TOKEN",
        "CODER_SESSION_TOKEN",
        "SSH_AUTH_SOCK",
        "KUBECONFIG"
    };

    private static readonly string[] SensitivePatterns =
    [
        "_API_KEY",
        "_SECRET",
        "_TOKEN",
        "_PASSWORD",
        "_AUTH",
        "_CREDENTIAL",
        "_PRIVATE_KEY"
    ];

    private static readonly HashSet<string> SafeKeyWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH",
        "HOME",
        "USER",
        "USERNAME",
        "USERPROFILE",
        "SHELL",
        "TERM",
        "LANG",
        "LC_ALL",
        "LC_CTYPE",
        "TEMP",
        "TMP",
        "TMPDIR",
        "SYSTEMROOT",
        "WINDIR",
        "APPDATA",
        "LOCALAPPDATA",
        "PROGRAMDATA",
        "PROGRAMFILES",
        "PROGRAMFILES(X86)",
        "COMMONPROGRAMFILES",
        "COMSPEC",
        "PATHEXT",
        "DOTNET_ROOT",
        "DOTNET_CLI_TELEMETRY_OPTOUT",
        "DOTNET_NOLOGO",
        "NODE_ENV",
        "NPM_CONFIG_CACHE",
        "CARGO_HOME",
        "RUSTUP_HOME",
        "GOPATH",
        "GOROOT",
        "PYTHONPATH"
    };

    public static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (SafeKeyWhitelist.Contains(key)) return false;
        if (SensitiveExactKeys.Contains(key)) return true;

        var upper = key.ToUpperInvariant();
        return SensitivePatterns.Any(pattern => upper.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    public static void Scrub(ProcessStartInfo psi)
    {
        ArgumentNullException.ThrowIfNull(psi);

        if (psi.Environment.Count == 0)
        {
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string k && entry.Value is string v)
                {
                    psi.Environment[k] = v;
                }
            }
        }

        var toRemove = psi.Environment.Keys.Cast<string>().Where(IsSensitiveKey).ToList();
        foreach (var key in toRemove)
        {
            psi.Environment.Remove(key);
        }
    }

    public static Dictionary<string, string?> CreateSanitizedEnvironment(IDictionary<string, string?> source)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in source)
        {
            if (!IsSensitiveKey(k))
            {
                result[k] = v;
            }
        }
        return result;
    }
}

public static class CommandClassifier
{
    private static readonly Regex PipeToShellRegex = new(@"\b(curl|wget)\b.*\|\s*(ba|z|da)?sh\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static CommandClassification Classify(string commandLine, string targetDirectory, string workspaceRoot, SecurityGuardSettings settings)
    {
        var trimmedCmd = (commandLine ?? "").Trim();
        var lowerCmd = trimmedCmd.ToLowerInvariant();

        // 1. Check Catastrophic Hard-Blocks
        foreach (var blocked in settings.BlockedPatterns)
        {
            if (lowerCmd.Contains(blocked.ToLowerInvariant()))
            {
                return new CommandClassification(
                    RiskLevel: CommandRiskLevel.Blocked,
                    OperationType: "Catastrophic Command",
                    IsNetworkAccess: false,
                    IsDestructive: true,
                    EstimatedImpact: $"Execution blocked by Security Policy: '{blocked}' is flagged as a catastrophic system command.",
                    RequiresApproval: false,
                    IsHardBlocked: true,
                    BlockReason: $"Execution blocked by Security Policy: '{blocked}' is flagged as a catastrophic system command.");
            }
        }

        if (PipeToShellRegex.IsMatch(trimmedCmd))
        {
            return new CommandClassification(
                RiskLevel: CommandRiskLevel.Blocked,
                OperationType: "Remote Script Execution",
                IsNetworkAccess: true,
                IsDestructive: true,
                EstimatedImpact: "Piping remote web content directly into a shell interpreter is strictly blocked.",
                RequiresApproval: false,
                IsHardBlocked: true,
                BlockReason: "Execution blocked: Piping untrusted remote scripts into shell is prohibited.");
        }

        // 2. Check Workspace Directory Sandbox Escape
        if (!string.IsNullOrWhiteSpace(workspaceRoot) && !string.IsNullOrWhiteSpace(targetDirectory))
        {
            try
            {
                var fullTarget = Path.GetFullPath(targetDirectory);
                var fullRoot = Path.GetFullPath(workspaceRoot);
                if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return new CommandClassification(
                        RiskLevel: CommandRiskLevel.Critical,
                        OperationType: "Out-of-Workspace Write",
                        IsNetworkAccess: false,
                        IsDestructive: false,
                        EstimatedImpact: $"Target directory '{targetDirectory}' escapes workspace root '{workspaceRoot}'.",
                        RequiresApproval: true,
                        IsHardBlocked: settings.EnforceWorkspaceSandboxing,
                        BlockReason: settings.EnforceWorkspaceSandboxing ? $"Target directory '{targetDirectory}' escapes workspace root." : null);
                }
            }
            catch { }
        }

        // 3. Check Redirection to Outside Workspace (e.g. > ../secret.txt)
        if (trimmedCmd.Contains('>') && !string.IsNullOrWhiteSpace(workspaceRoot))
        {
            var redirIndex = trimmedCmd.IndexOf('>');
            var redirPath = trimmedCmd[(redirIndex + 1)..].Trim().TrimStart('>').Trim().Split(' ', '\t', '|', ';', '&')[0].Trim('"', '\'');
            if (redirPath.StartsWith("..") || Path.IsPathRooted(redirPath))
            {
                try
                {
                    var fullRedir = Path.GetFullPath(Path.Combine(targetDirectory, redirPath));
                    var fullRoot = Path.GetFullPath(workspaceRoot);
                    if (!fullRedir.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return new CommandClassification(
                            RiskLevel: CommandRiskLevel.Critical,
                            OperationType: "Out-of-Workspace Write",
                            IsNetworkAccess: false,
                            IsDestructive: false,
                            EstimatedImpact: $"Command attempts to redirect output outside the authorized workspace to '{redirPath}'.",
                            RequiresApproval: true,
                            IsHardBlocked: settings.EnforceWorkspaceSandboxing,
                            BlockReason: settings.EnforceWorkspaceSandboxing ? $"Output redirection outside workspace root is blocked." : null);
                    }
                }
                catch { }
            }
        }

        // 4. Analyze Sub-commands (split by pipe or sequence)
        var subCommands = SplitCommandChain(trimmedCmd);
        bool hasNetwork = false;
        bool hasDestructive = false;
        bool isDependencyInstall = false;
        string? networkDetails = null;
        string? destructiveDetails = null;

        foreach (var sub in subCommands)
        {
            var tokens = Tokenize(sub);
            if (tokens.Count == 0) continue;

            var (bin, args) = ExtractBinaryAndArgs(tokens);

            // Check Deletions / Destructive file operations
            if (IsDestructiveTool(bin, args))
            {
                hasDestructive = true;
                destructiveDetails = $"Permanently deletes files or directories via '{bin}'.";
            }

            // Check Network access & package management
            var (isNet, isDep, details) = CheckNetworkAccess(bin, args);
            if (isNet)
            {
                hasNetwork = true;
                if (isDep) isDependencyInstall = true;
                networkDetails = details;
            }
        }

        if (hasNetwork)
        {
            var opType = isDependencyInstall ? "Install Dependencies" : "Network Request";
            var impact = networkDetails ?? (isDependencyInstall
                ? "Modifies project dependencies and downloads remote packages over the network."
                : "Initiates outbound network connection to fetch or transmit remote data.");

            return new CommandClassification(
                RiskLevel: CommandRiskLevel.High,
                OperationType: opType,
                IsNetworkAccess: true,
                IsDestructive: hasDestructive,
                EstimatedImpact: impact,
                RequiresApproval: true,
                IsHardBlocked: false);
        }

        if (hasDestructive)
        {
            return new CommandClassification(
                RiskLevel: CommandRiskLevel.High,
                OperationType: "Delete File",
                IsNetworkAccess: false,
                IsDestructive: true,
                EstimatedImpact: destructiveDetails ?? "Permanently deletes files or directories from the filesystem.",
                RequiresApproval: true,
                IsHardBlocked: false);
        }

        // 5. Check Safe Read-Only Inspection
        if (subCommands.All(IsSafeReadOnly))
        {
            return new CommandClassification(
                RiskLevel: CommandRiskLevel.Low,
                OperationType: "Read / Inspect",
                IsNetworkAccess: false,
                IsDestructive: false,
                EstimatedImpact: "Inspects filesystem or environment state without mutations.",
                RequiresApproval: false,
                IsHardBlocked: false);
        }

        // 6. Check Safe Build / Test
        if (subCommands.All(s => IsSafeBuildOrTest(s, settings)))
        {
            return new CommandClassification(
                RiskLevel: CommandRiskLevel.Low,
                OperationType: "Build / Test",
                IsNetworkAccess: false,
                IsDestructive: false,
                EstimatedImpact: "Compiles or executes project test suites.",
                RequiresApproval: false,
                IsHardBlocked: false);
        }

        // 7. General / Arbitrary Shell Command
        return new CommandClassification(
            RiskLevel: CommandRiskLevel.High,
            OperationType: "Execute Shell Command",
            IsNetworkAccess: false,
            IsDestructive: false,
            EstimatedImpact: "Executes a shell command in the local workspace.",
            RequiresApproval: true,
            IsHardBlocked: false);
    }

    private static List<string> SplitCommandChain(string command)
    {
        var result = new List<string>();
        var parts = Regex.Split(command, @"(&&|\|\||;|\|)");
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed is "&&" or "||" or ";" or "|") continue;
            if (!string.IsNullOrWhiteSpace(trimmed)) result.Add(trimmed);
        }
        return result.Count > 0 ? result : [command.Trim()];
    }

    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var matches = Regex.Matches(command, @"[\""].+?[\""]|[^ \t\r\n]+");
        foreach (Match match in matches)
        {
            var val = match.Value.Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(val)) tokens.Add(val);
        }
        return tokens;
    }

    private static (string Binary, List<string> Args) ExtractBinaryAndArgs(List<string> tokens)
    {
        var index = 0;
        // Skip common wrapper binaries
        while (index < tokens.Count && tokens[index] is "sudo" or "env" or "nohup" or "time")
        {
            index++;
        }

        if (index >= tokens.Count) return ("", []);
        var bin = Path.GetFileName(tokens[index]).ToLowerInvariant();
        if (bin.EndsWith(".exe")) bin = bin[..^4];
        var args = tokens.Skip(index + 1).ToList();
        return (bin, args);
    }

    private static bool IsDestructiveTool(string bin, List<string> args)
    {
        if (bin is "rm" or "rmdir" or "del" or "erase" or "ri" or "remove-item" or "unlink")
            return true;

        if (bin == "git" && args.Count > 0)
        {
            var sub = args[0].ToLowerInvariant();
            if (sub is "rm" or "clean") return true;
        }

        return false;
    }

    private static (bool IsNetwork, bool IsDependency, string? Details) CheckNetworkAccess(string bin, List<string> args)
    {
        // Standalone network download / fetch tools
        if (bin is "curl" or "wget" or "http" or "httpie" or "fetch" or "iwr" or "irm" or "invoke-webrequest" or "invoke-restmethod"
            or "ssh" or "scp" or "sftp" or "rsync" or "nc" or "netcat" or "ncat" or "telnet" or "ftp"
            or "ping" or "nslookup" or "dig" or "traceroute" or "tracert")
        {
            return (true, false, $"Outbound network connection requested via '{bin}'.");
        }

        var sub = args.Count > 0 ? args[0].ToLowerInvariant() : "";

        // VCS network operations
        if (bin == "git" && sub is "clone" or "fetch" or "pull" or "push" or "submodule")
        {
            return (true, false, $"Remote repository synchronization via 'git {sub}'.");
        }

        // Container image pulls/pushes
        if (bin is "docker" or "podman" && sub is "pull" or "push" or "login")
        {
            return (true, false, $"Remote container registry operation via '{bin} {sub}'.");
        }

        // Package managers installing/updating dependencies
        if (bin == "npm" && sub is "install" or "i" or "add" or "update")
            return (true, true, $"Installs package dependencies via 'npm {sub}'.");

        if (bin == "pnpm" && sub is "install" or "i" or "add" or "update")
            return (true, true, $"Installs package dependencies via 'pnpm {sub}'.");

        if (bin == "yarn" && sub is "add" or "install" or "upgrade")
            return (true, true, $"Installs package dependencies via 'yarn {sub}'.");

        if (bin is "pip" or "pip3" && sub is "install" or "download")
            return (true, true, $"Downloads Python packages via '{bin} {sub}'.");

        if (bin == "poetry" && sub is "add" or "install" or "update")
            return (true, true, $"Manages dependencies via 'poetry {sub}'.");

        if (bin == "pipenv" && sub is "install" or "update")
            return (true, true, $"Manages dependencies via 'pipenv {sub}'.");

        if (bin == "cargo" && sub is "add" or "install")
            return (true, true, $"Downloads Rust crates via 'cargo {sub}'.");

        if (bin == "dotnet")
        {
            if (sub == "add" && args.Count > 1 && args[1].Equals("package", StringComparison.OrdinalIgnoreCase))
                return (true, true, "Adds NuGet dependency via 'dotnet add package'.");
            if (sub == "tool" && args.Count > 1 && args[1].Equals("install", StringComparison.OrdinalIgnoreCase))
                return (true, true, "Downloads .NET tool via 'dotnet tool install'.");
        }

        if (bin == "go" && sub is "get" or "install")
            return (true, true, $"Downloads Go modules via 'go {sub}'.");

        if (bin == "composer" && sub is "require" or "install" or "update")
            return (true, true, $"Installs PHP packages via 'composer {sub}'.");

        if (bin is "brew" or "apt" or "apt-get" or "apk" or "choco" or "winget" && sub is "install" or "add")
            return (true, true, $"Installs system packages via '{bin} {sub}'.");

        return (false, false, null);
    }

    private static bool IsSafeReadOnly(string command)
    {
        var lower = command.Trim().ToLowerInvariant();
        string[] safeInspectionPrefixes =
        [
            "git status", "git diff", "git log", "git branch", "git show", "git rev-parse",
            "ls", "dir", "pwd", "echo", "cat ", "head ", "tail ", "grep ", "rg ", "find ",
            "which ", "where ", "type ", "wc "
        ];

        return safeInspectionPrefixes.Any(p => lower.StartsWith(p) || lower == p.Trim());
    }

    private static bool IsSafeBuildOrTest(string command, SecurityGuardSettings settings)
    {
        var lower = command.Trim().ToLowerInvariant();
        if (settings.SafeCommandPrefixes.Any(p => lower.StartsWith(p.ToLowerInvariant())))
            return true;

        string[] safeBuildPrefixes =
        [
            "pytest", "python -m unittest", "go test", "go build", "cargo check", "cargo build", "cargo test"
        ];

        return safeBuildPrefixes.Any(lower.StartsWith);
    }
}

public sealed class PermissionCheckResult
{
    public bool IsAllowed { get; set; } = true;
    public bool RequiresUserConfirmation { get; set; }
    public string? Reason { get; set; }
    public bool IsBlockedBySecurityPolicy { get; set; }
    public CommandClassification? Classification { get; set; }
    public string? OperationType { get; set; }
    public string? ExactTarget { get; set; }
    public string? WorkspaceRoot { get; set; }
    public bool IsNetworkAccess { get; set; }
    public string? EstimatedImpact { get; set; }
    public CommandRiskLevel RiskLevel { get; set; } = CommandRiskLevel.Low;
}

public sealed record PendingPermissionRequest(
    string Id,
    string RunId,
    string ToolName,
    string? Command,
    string? FilePath,
    string? WorkingDirectory,
    string Reason,
    DateTimeOffset RequestedAt,
    string? OperationType = null,
    string? ExactTarget = null,
    string? WorkspaceRoot = null,
    bool IsNetworkAccess = false,
    string? EstimatedImpact = null,
    CommandRiskLevel RiskLevel = CommandRiskLevel.High);

public sealed record PermissionDecision(bool Approved, bool RememberForSession = false);

public sealed record PermissionResponseRequest(bool Approved, bool RememberForSession = false);

public sealed class AgentSecurityService
{
    private readonly StorageService _storageService;
    private readonly string _settingsFilePath;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (PendingPermissionRequest Request, TaskCompletionSource<PermissionDecision> Tcs)> _pending = new();
    private readonly HashSet<string> _sessionAllowedCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sessionAllowedFiles = new(StringComparer.OrdinalIgnoreCase);

    public SecurityGuardSettings Settings { get; private set; } = new();
    public event Action? OnChange;
    public event Action<PendingPermissionRequest>? OnPermissionRequested;

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

    public CommandClassification ClassifyCommand(string commandLine, string targetDirectory, string workspaceRoot) =>
        CommandClassifier.Classify(commandLine, targetDirectory, workspaceRoot, Settings);

    public PermissionCheckResult EvaluateCommand(string commandLine, string targetDirectory, string workspaceRoot)
    {
        var trimmedCmd = (commandLine ?? "").Trim();
        var classification = ClassifyCommand(trimmedCmd, targetDirectory, workspaceRoot);

        var result = new PermissionCheckResult
        {
            Classification = classification,
            OperationType = classification.OperationType,
            ExactTarget = trimmedCmd,
            WorkspaceRoot = workspaceRoot,
            IsNetworkAccess = classification.IsNetworkAccess,
            EstimatedImpact = classification.EstimatedImpact,
            RiskLevel = classification.RiskLevel
        };

        // 1. Hard-blocked commands are unconditionally rejected
        if (Settings.BlockDestructiveCommands && classification.IsHardBlocked)
        {
            result.IsAllowed = false;
            result.IsBlockedBySecurityPolicy = true;
            result.Reason = classification.BlockReason ?? $"Execution blocked by Security Policy: '{trimmedCmd}' is flagged as a prohibited command.";
            return result;
        }

        // 2. Directory sandbox escape checks
        if (classification.RiskLevel == CommandRiskLevel.Critical && classification.OperationType == "Out-of-Workspace Write")
        {
            if (Settings.EnforceWorkspaceSandboxing)
            {
                result.IsAllowed = false;
                result.IsBlockedBySecurityPolicy = true;
                result.Reason = $"Execution blocked: Target directory or file path escapes authorized workspace root '{workspaceRoot}'.";
                return result;
            }

            // If sandboxing enforcement is disabled, still require interactive confirmation (jailbreak alert)
            result.IsAllowed = true;
            result.RequiresUserConfirmation = true;
            result.Reason = $"Target operation escapes workspace root '{workspaceRoot}' (Jailbreak alert).";
            return result;
        }

        // 3. Session-approved commands
        if (IsSessionAllowedCommand(trimmedCmd))
        {
            result.IsAllowed = true;
            result.RequiresUserConfirmation = false;
            return result;
        }

        // 4. Policy evaluation by permission mode
        switch (Settings.PermissionMode)
        {
            case ExecutionPermissionMode.FullAutonomous:
                result.IsAllowed = true;
                result.RequiresUserConfirmation = false;
                break;

            case ExecutionPermissionMode.SafeAutoApprove:
                if (classification.IsNetworkAccess)
                {
                    result.IsAllowed = true;
                    result.RequiresUserConfirmation = true;
                    result.Reason = $"Outbound network access requires user confirmation ({classification.OperationType}).";
                }
                else if (classification.IsDestructive)
                {
                    result.IsAllowed = true;
                    result.RequiresUserConfirmation = true;
                    result.Reason = $"File deletion or destructive operation requires confirmation ({classification.OperationType}).";
                }
                else if (classification.RiskLevel == CommandRiskLevel.Low)
                {
                    result.IsAllowed = true;
                    result.RequiresUserConfirmation = false;
                }
                else
                {
                    result.IsAllowed = true;
                    result.RequiresUserConfirmation = true;
                    result.Reason = $"Command '{trimmedCmd}' requires confirmation ({classification.OperationType}).";
                }
                break;

            case ExecutionPermissionMode.AskAlways:
                result.IsAllowed = true;
                result.RequiresUserConfirmation = true;
                result.Reason = $"Command '{trimmedCmd}' requires approval before execution.";
                break;
        }

        return result;
    }

    public PermissionCheckResult EvaluateFileAccess(string filePath, string workspaceRoot, bool isWrite)
    {
        var cleanPath = (filePath ?? "").Trim().Replace('\\', '/');
        var fullTarget = "";
        var isEscape = false;

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            try
            {
                fullTarget = Path.GetFullPath(Path.Combine(workspaceRoot, cleanPath));
                var fullRoot = Path.GetFullPath(workspaceRoot);
                isEscape = !fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                return new PermissionCheckResult
                {
                    IsAllowed = false,
                    IsBlockedBySecurityPolicy = true,
                    Reason = $"Invalid path: {ex.Message}",
                    RiskLevel = CommandRiskLevel.Critical,
                    OperationType = isWrite ? "Out-of-Workspace Write" : "Read File",
                    ExactTarget = cleanPath,
                    WorkspaceRoot = workspaceRoot
                };
            }
        }

        if (isEscape)
        {
            if (Settings.EnforceWorkspaceSandboxing)
            {
                return new PermissionCheckResult
                {
                    IsAllowed = false,
                    IsBlockedBySecurityPolicy = true,
                    Reason = $"Path traversal blocked: '{cleanPath}' escapes workspace root.",
                    RiskLevel = CommandRiskLevel.Critical,
                    OperationType = isWrite ? "Out-of-Workspace Write" : "Read File",
                    ExactTarget = cleanPath,
                    WorkspaceRoot = workspaceRoot
                };
            }

            return new PermissionCheckResult
            {
                IsAllowed = true,
                RequiresUserConfirmation = true,
                Reason = $"Path '{cleanPath}' is outside the authorized workspace root (Jailbreak alert).",
                RiskLevel = CommandRiskLevel.Critical,
                OperationType = isWrite ? "Out-of-Workspace Write" : "Read File",
                ExactTarget = cleanPath,
                WorkspaceRoot = workspaceRoot,
                EstimatedImpact = "Modifies files outside authorized workspace."
            };
        }

        if (!isWrite)
        {
            return new PermissionCheckResult
            {
                IsAllowed = true,
                RequiresUserConfirmation = false,
                RiskLevel = CommandRiskLevel.Low,
                OperationType = "Read File",
                ExactTarget = cleanPath,
                WorkspaceRoot = workspaceRoot
            };
        }

        // File Write / Modification
        if (IsSessionAllowedFile(cleanPath))
        {
            return new PermissionCheckResult
            {
                IsAllowed = true,
                RequiresUserConfirmation = false,
                RiskLevel = CommandRiskLevel.Medium,
                OperationType = "Modify File",
                ExactTarget = cleanPath,
                WorkspaceRoot = workspaceRoot
            };
        }

        switch (Settings.PermissionMode)
        {
            case ExecutionPermissionMode.FullAutonomous:
            case ExecutionPermissionMode.SafeAutoApprove:
                // Per Section 2 of Blueprint: In-Workspace File Writes & Edits are automatic by default
                return new PermissionCheckResult
                {
                    IsAllowed = true,
                    RequiresUserConfirmation = false,
                    RiskLevel = CommandRiskLevel.Medium,
                    OperationType = "Modify File",
                    ExactTarget = cleanPath,
                    WorkspaceRoot = workspaceRoot,
                    EstimatedImpact = $"Modifies in-workspace file '{cleanPath}'."
                };

            case ExecutionPermissionMode.AskAlways:
                return new PermissionCheckResult
                {
                    IsAllowed = true,
                    RequiresUserConfirmation = true,
                    Reason = $"Modifying '{cleanPath}' requires user approval.",
                    RiskLevel = CommandRiskLevel.Medium,
                    OperationType = "Modify File",
                    ExactTarget = cleanPath,
                    WorkspaceRoot = workspaceRoot,
                    EstimatedImpact = $"Modifies in-workspace file '{cleanPath}'."
                };

            default:
                return new PermissionCheckResult { IsAllowed = true };
        }
    }

    public void ScrubEnvironment(ProcessStartInfo psi)
    {
        if (Settings.ScrubChildProcessEnvironment)
        {
            EnvironmentScrubber.Scrub(psi);
        }
    }

    public bool IsSessionAllowedCommand(string command)
    {
        var trimmed = command.Trim();
        var lower = trimmed.ToLowerInvariant();
        return _sessionAllowedCommands.Contains(trimmed) || _sessionAllowedCommands.Any(p => lower.StartsWith(p.ToLowerInvariant()));
    }

    public bool IsSessionAllowedFile(string filePath)
    {
        var clean = filePath.Trim().Replace('\\', '/');
        return _sessionAllowedFiles.Contains(clean) || _sessionAllowedFiles.Contains(Path.GetFileName(clean));
    }

    public void ClearSessionAllowances()
    {
        _sessionAllowedCommands.Clear();
        _sessionAllowedFiles.Clear();
    }

    public async Task<PermissionDecision> RequestApprovalAsync(
        string runId,
        string toolName,
        string? command,
        string? filePath,
        string? workingDirectory,
        string reason,
        string? operationType = null,
        string? exactTarget = null,
        string? workspaceRoot = null,
        bool isNetworkAccess = false,
        string? estimatedImpact = null,
        CommandRiskLevel riskLevel = CommandRiskLevel.High,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var request = new PendingPermissionRequest(
            Id: id,
            RunId: runId,
            ToolName: toolName,
            Command: command,
            FilePath: filePath,
            WorkingDirectory: workingDirectory,
            Reason: reason,
            RequestedAt: DateTimeOffset.UtcNow,
            OperationType: operationType ?? (toolName.Contains("command") ? "Execute Command" : "Modify File"),
            ExactTarget: exactTarget ?? command ?? filePath,
            WorkspaceRoot: workspaceRoot ?? workingDirectory,
            IsNetworkAccess: isNetworkAccess,
            EstimatedImpact: estimatedImpact ?? reason,
            RiskLevel: riskLevel);

        var tcs = new TaskCompletionSource<PermissionDecision>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[id] = (request, tcs);
        OnPermissionRequested?.Invoke(request);

        var timeoutSecs = Math.Max(Settings.CommandTimeoutSeconds, 90);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSecs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        using var reg = linked.Token.Register(() => tcs.TrySetResult(new PermissionDecision(false, false)));

        try
        {
            var decision = await tcs.Task;
            if (decision.Approved && decision.RememberForSession)
            {
                if (!string.IsNullOrWhiteSpace(command)) _sessionAllowedCommands.Add(command.Trim());
                if (!string.IsNullOrWhiteSpace(filePath)) _sessionAllowedFiles.Add(filePath.Trim().Replace('\\', '/'));
            }
            return decision;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public bool RespondApproval(string id, bool approved, bool rememberForSession = false)
    {
        if (_pending.TryGetValue(id, out var item))
        {
            return item.Tcs.TrySetResult(new PermissionDecision(approved, rememberForSession));
        }
        return false;
    }

    public IReadOnlyList<PendingPermissionRequest> GetPendingRequests() =>
        _pending.Values.Select(v => v.Request).ToList();
}
