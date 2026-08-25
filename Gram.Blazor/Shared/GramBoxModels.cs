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

public sealed class GramBoxConfig
{
    public GramBoxEngineType Engine { get; set; } = GramBoxEngineType.Auto;
    public string BaseStoragePath { get; set; } = "";
    public bool EnableLowCpuPriority { get; set; } = true;
    public int MaxProcessTimeoutSeconds { get; set; } = 180;
    public int MaxMemoryQuotaMb { get; set; } = 2048;
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
