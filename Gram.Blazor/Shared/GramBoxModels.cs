using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public enum GramBoxEngineType
{
    Auto,
    NativeWorktree,
    Wsl2,
    OciContainer
}

public enum SandboxStatus
{
    Idle,
    RunningCommand,
    Ready,
    Merged,
    Error,
    Disposed
}

public enum SandboxNetworkMode { Disabled, Allowlisted, Unrestricted }

public sealed class GramBoxConfig
{
    public GramBoxEngineType Engine { get; set; } = GramBoxEngineType.Auto;
    public string BaseStoragePath { get; set; } = "";
    public bool EnableLowCpuPriority { get; set; } = true;
    public int MaxProcessTimeoutSeconds { get; set; } = 180;
    public int MaxMemoryQuotaMb { get; set; } = 2048;
    public int MaxOutputBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxChildProcesses { get; set; } = 128;
    public bool EnableUnixResourceLimits { get; set; } = true;
    public SandboxNetworkMode DefaultNetworkMode { get; set; } = SandboxNetworkMode.Disabled;
    public List<string> AllowedNetworkHosts { get; set; } = [];
    public bool StripSensitiveEnvVars { get; set; } = true;
    public bool SharedDependencyCaching { get; set; } = true;
    public bool UseCopyOnWriteCloning { get; set; } = true;
}

public sealed class GramBoxSandboxSession
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BranchName { get; set; } = "";
    public string WorktreePath { get; set; } = "";
    public string BaseRepoPath { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;
    public SandboxStatus Status { get; set; } = SandboxStatus.Idle;
    public string? CurrentCommand { get; set; }
    public GramBoxEngineType DriverType { get; set; } = GramBoxEngineType.NativeWorktree;
    public long DiskUsageBytes { get; set; }
    public string? LastError { get; set; }
    public string? LeaseId { get; set; }
    public string? RunId { get; set; }
    public SandboxResourceBudget? ResourceBudget { get; set; }
    public SandboxNetworkMode NetworkMode { get; set; } = SandboxNetworkMode.Disabled;
    public List<string> AllowedNetworkHosts { get; set; } = [];
}

public sealed class GramBoxExecutionResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = "";
    public string Stderr { get; set; } = "";
    public long ElapsedMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SandboxId { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public long CpuTimeMs { get; set; }
    public int OutputBytes { get; set; }
}

public sealed class SandboxDiffSummary
{
    public string SandboxId { get; set; } = "";
    public string BranchName { get; set; } = "";
    public int FilesChanged { get; set; }
    public int Insertions { get; set; }
    public int Deletions { get; set; }
    public string DiffContent { get; set; } = "";
}

public sealed class SandboxMergePreview
{
    public string SandboxId { get; init; } = "";
    public string SourceBranch { get; init; } = "";
    public string TargetRef { get; init; } = "HEAD";
    public bool CanMergeCleanly { get; init; }
    public List<string> ChangedFiles { get; init; } = [];
    public string ConflictOutput { get; init; } = "";
    public string DiffContent { get; init; } = "";
}

public sealed class SandboxCheckpoint
{
    public string Id { get; init; } = "cp-" + Guid.NewGuid().ToString("N")[..12];
    public string SandboxId { get; init; } = "";
    public string? RunId { get; init; }
    public string Reason { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string DiffPath { get; init; } = "";
    public int FilesChanged { get; init; }
}
