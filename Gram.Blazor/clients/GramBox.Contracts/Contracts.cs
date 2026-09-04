namespace GramBox.Contracts;

public enum GramBoxEngine { Auto, NativeWorktree, OciContainer }
public enum SandboxStatus { Idle, Running, Ready, Completed, Failed, Cancelled, TimedOut, Disposed }
public enum NetworkMode { Disabled, Allowlisted, Unrestricted }
public enum ResourceClass { Tiny, Small, Medium, Large }

public sealed record SandboxResourceBudget(ResourceClass Class, int CpuCores, long MemoryBytes, long DiskBytes);

public sealed record CreateSandboxRequest(
    string RepositoryPath,
    string? RunId = null,
    string? Name = null,
    GramBoxEngine Engine = GramBoxEngine.Auto,
    ResourceClass ResourceClass = ResourceClass.Small,
    NetworkMode NetworkMode = NetworkMode.Disabled,
    IReadOnlyList<string>? AllowedNetworkHosts = null);

public sealed record SandboxInfo(
    string Id,
    string RunId,
    string Name,
    string RootPath,
    GramBoxEngine Engine,
    SandboxStatus Status,
    NetworkMode NetworkMode,
    SandboxResourceBudget? Budget,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt,
    string IsolationLevel);

public sealed record ExecuteCommandRequest(string Command, string? WorkingDirectory = null, int TimeoutSeconds = 120);

public sealed record ExecutionResult(
    bool Success,
    int ExitCode,
    string Stdout,
    string Stderr,
    long ElapsedMilliseconds,
    string? Error = null,
    long PeakWorkingSetBytes = 0,
    long CpuTimeMilliseconds = 0,
    int OutputBytes = 0);

public sealed record FileWriteRequest(string RelativePath, string Content);
public sealed record FileReadResult(string RelativePath, string? Content, bool Found);

public sealed record DiffSummary(
    string SandboxId,
    string BranchName,
    int FilesChanged,
    int Insertions,
    int Deletions,
    string DiffContent);

public sealed record SandboxEvent(long Sequence, string SandboxId, string Type, string? Message = null, DateTimeOffset? Timestamp = null);
public sealed record SandboxCapabilities(GramBoxEngine[] AvailableEngines, bool HardResourceLimits, bool NetworkIsolation, string EnforcementMode, bool Enabled = true, string? DisabledReason = null);
public enum SandboxJobStatus { Queued, Starting, Ready, Failed, Cancelled }
public enum SandboxJobPriority { Interactive = 0, Normal = 1, Background = 2, Batch = 3 }
public enum SandboxResourceClass { Tiny, Small, Medium, Large }
public sealed record SandboxJobRequest(string RepositoryPath, string? RunId = null, string? Name = null, string Engine = "Auto", string NetworkMode = "Disabled", IReadOnlyList<string>? AllowedNetworkHosts = null, string? IdempotencyKey = null, SandboxJobPriority Priority = SandboxJobPriority.Normal, SandboxResourceClass ResourceClass = SandboxResourceClass.Small, int MaxRuntimeSeconds = 180);
public sealed record SandboxJobSnapshot(string JobId, SandboxJobStatus Status, int? QueuePosition, string? SandboxId, string? Error, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, SandboxJobPriority Priority = SandboxJobPriority.Normal, SandboxResourceClass ResourceClass = SandboxResourceClass.Small, int MaxRuntimeSeconds = 180);
public sealed record ApiError(string Code, string Message, string? TraceId = null);
