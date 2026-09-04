namespace Gram.Blazor.Shared;

public sealed record GramBoxCreateRequest(
    string RepositoryPath,
    string? RunId = null,
    string? Name = null,
    string Engine = "Auto",
    string ResourceClass = "Small",
    string NetworkMode = "Disabled",
    IReadOnlyList<string>? AllowedNetworkHosts = null);

public sealed record GramBoxCommandRequest(string Command, string? WorkingDirectory = null, int TimeoutSeconds = 120);
public sealed record GramBoxFileWriteRequest(string RelativePath, string? Content);

public static class GramBoxApiMapper
{
    public static SandboxNetworkMode ParseNetworkMode(string? value) =>
        Enum.TryParse<SandboxNetworkMode>(value, true, out var mode) ? mode : SandboxNetworkMode.Disabled;

    public static object ToPublic(GramBoxSandboxSession session) => new
    {
        id = session.Id,
        runId = session.RunId ?? "",
        name = session.Name,
        rootPath = session.WorktreePath,
        engine = "NativeWorktree",
        status = session.Status == SandboxStatus.Idle ? "Ready" : session.Status.ToString(),
        networkMode = session.NetworkMode.ToString(),
        budget = session.ResourceBudget is null ? null : new
        {
            @class = session.ResourceBudget.Class.ToString(),
            cpuCores = session.ResourceBudget.CpuCores,
            memoryBytes = session.ResourceBudget.MemoryBytes,
            diskBytes = session.ResourceBudget.DiskBytes
        },
        createdAt = new DateTimeOffset(session.CreatedAt),
        lastActiveAt = new DateTimeOffset(session.LastActiveAt),
        isolationLevel = "Git worktree + monitored native process (not an OS security boundary)"
    };
}
