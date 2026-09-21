namespace Gram.Blazor.Shared;

public sealed record WorkspaceFileCheckpoint(
    string Id,
    string WorkspacePath,
    string RelativePath,
    string? OriginalContent,
    bool ExistedPrior,
    DateTimeOffset Timestamp,
    string Reason,
    string? RunId = null);
