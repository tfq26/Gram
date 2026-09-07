namespace Gram.Blazor.Shared;

public sealed record FilePickRequest(
    string? WorkspaceRoot,
    string RelativePath,
    int MaxBytes = 1_000_000
);

public sealed record FilePickResult(
    bool Success,
    string? FilePath,
    string? Content,
    long SizeBytes,
    string? Error = null
);

public sealed record TerminalRunRequest(
    string Command,
    string? WorkspaceRoot = null,
    int TimeoutSeconds = 60,
    string? RunId = null
);

public sealed record LocalSandboxCreateRequest(
    string? Name,
    string? RunId,
    string? BaseDirectory = null
);

public sealed record LocalSandboxCreateResult(
    bool Success,
    string SandboxId,
    string SandboxPath,
    string? Error = null
);
