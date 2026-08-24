using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public enum GitProviderType
{
    GitHub,
    GitLab,
    Forgejo,
    Bitbucket,
    LocalGit
}

public sealed class GitAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public GitProviderType ProviderType { get; set; } = GitProviderType.GitHub;
    public string DisplayName { get; set; } = "";
    public string InstanceUrl { get; set; } = "https://github.com";
    public string Username { get; set; } = "";
    public string Token { get; set; } = "";
    public bool IsDefault { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GitRepository
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Description { get; set; } = "";
    public string DefaultBranch { get; set; } = "main";
    public bool IsPrivate { get; set; }
    public string CloneUrl { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public string Owner { get; set; } = "";
}

public sealed class GitBranch
{
    public string Name { get; set; } = "";
    public string CommitSha { get; set; } = "";
    public bool IsDefault { get; set; }
}

public sealed class GitFileItem
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "file"; // "file", "directory"
    public long Size { get; set; }
    public string Sha { get; set; } = "";
}

public sealed class FileChange
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
    public string Action { get; set; } = "update"; // "create", "update", "delete"
}

public sealed class GitCommitResult
{
    public string Sha { get; set; } = "";
    public string Message { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? HtmlUrl { get; set; }
}

public sealed class GitPullRequest
{
    public string Id { get; set; } = "";
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string HeadBranch { get; set; } = "";
    public string BaseBranch { get; set; } = "";
    public string State { get; set; } = "open";
    public string HtmlUrl { get; set; } = "";
}
