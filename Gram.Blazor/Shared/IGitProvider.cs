namespace Gram.Blazor.Shared;

public interface IGitProvider
{
    GitProviderType ProviderType { get; }
    string Name { get; }
    string DefaultInstanceUrl { get; }

    Task<bool> ValidateCredentialsAsync(GitAccount account, CancellationToken cancellationToken = default);
    Task<GitUserProfile> GetUserProfileAsync(GitAccount account, CancellationToken cancellationToken = default);
    Task<List<GitRepository>> ListRepositoriesAsync(GitAccount account, CancellationToken cancellationToken = default);
    Task<List<GitBranch>> ListBranchesAsync(GitAccount account, string repoFullName, CancellationToken cancellationToken = default);
    Task<string> GetFileContentAsync(GitAccount account, string repoFullName, string branch, string filePath, CancellationToken cancellationToken = default);
    Task<List<GitFileItem>> ListDirectoryAsync(GitAccount account, string repoFullName, string branch, string path = "", CancellationToken cancellationToken = default);
    Task<GitCommitResult> CommitChangesAsync(GitAccount account, string repoFullName, string branch, string commitMessage, List<FileChange> changes, CancellationToken cancellationToken = default);
    Task<GitPullRequest> CreatePullRequestAsync(GitAccount account, string repoFullName, string title, string headBranch, string baseBranch, string description, CancellationToken cancellationToken = default);
}
