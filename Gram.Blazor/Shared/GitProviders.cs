using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gram.Blazor.Shared;

public sealed class GitHubProvider : IGitProvider
{
    private readonly HttpClient _httpClient;

    public GitProviderType ProviderType => GitProviderType.GitHub;
    public string Name => "GitHub";
    public string DefaultInstanceUrl => "https://github.com";

    public GitHubProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Gram-Blazor", "1.0"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return req;
    }

    private string GetApiBase(string instanceUrl)
    {
        var trimmed = (instanceUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Equals("https://github.com", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.github.com";
        }
        // GitHub Enterprise Server
        return $"{trimmed}/api/v3";
    }

    public async Task<bool> ValidateCredentialsAsync(GitAccount account, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/user", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<GitRepository>> ListRepositoriesAsync(GitAccount account, CancellationToken cancellationToken = default)
    {
        var list = new List<GitRepository>();
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/user/repos?per_page=100&sort=updated", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                list.Add(new GitRepository
                {
                    Id = item["id"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    FullName = item["full_name"]?.ToString() ?? "",
                    Description = item["description"]?.ToString() ?? "",
                    DefaultBranch = item["default_branch"]?.ToString() ?? "main",
                    IsPrivate = item["private"]?.GetValue<bool>() ?? false,
                    CloneUrl = item["clone_url"]?.ToString() ?? "",
                    HtmlUrl = item["html_url"]?.ToString() ?? "",
                    Owner = item["owner"]?["login"]?.ToString() ?? ""
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitHubProvider] Error listing repositories: {ex.Message}");
        }
        return list;
    }

    public async Task<List<GitBranch>> ListBranchesAsync(GitAccount account, string repoFullName, CancellationToken cancellationToken = default)
    {
        var list = new List<GitBranch>();
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/repos/{repoFullName}/branches?per_page=100", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                list.Add(new GitBranch
                {
                    Name = item["name"]?.ToString() ?? "",
                    CommitSha = item["commit"]?["sha"]?.ToString() ?? "",
                    IsDefault = false
                });
            }
        }
        catch {}
        return list;
    }

    public async Task<string> GetFileContentAsync(GitAccount account, string repoFullName, string branch, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/repos/{repoFullName}/contents/{filePath.TrimStart('/')}?ref={branch}", account.Token);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.raw"));

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return $"Error fetching file from GitHub: {ex.Message}";
        }
        return "";
    }

    public async Task<List<GitFileItem>> ListDirectoryAsync(GitAccount account, string repoFullName, string branch, string path = "", CancellationToken cancellationToken = default)
    {
        var list = new List<GitFileItem>();
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            var targetPath = string.IsNullOrWhiteSpace(path) ? "" : $"/{path.TrimStart('/')}";
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/repos/{repoFullName}/contents{targetPath}?ref={branch}", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                var typeStr = item["type"]?.ToString() ?? "file";
                list.Add(new GitFileItem
                {
                    Path = item["path"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    Type = typeStr == "dir" ? "directory" : "file",
                    Size = item["size"]?.GetValue<long>() ?? 0,
                    Sha = item["sha"]?.ToString() ?? ""
                });
            }
        }
        catch {}
        return list;
    }

    public async Task<GitCommitResult> CommitChangesAsync(GitAccount account, string repoFullName, string branch, string commitMessage, List<FileChange> changes, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            foreach (var change in changes)
            {
                var targetPath = change.Path.TrimStart('/');
                string? currentSha = null;
                try
                {
                    using var checkReq = CreateRequest(HttpMethod.Get, $"{apiBase}/repos/{repoFullName}/contents/{targetPath}?ref={branch}", account.Token);
                    using var checkRes = await _httpClient.SendAsync(checkReq, cancellationToken);
                    if (checkRes.IsSuccessStatusCode)
                    {
                        var checkJson = await checkRes.Content.ReadAsStringAsync(cancellationToken);
                        currentSha = JsonNode.Parse(checkJson)?["sha"]?.ToString();
                    }
                }
                catch {}

                var payload = new JsonObject
                {
                    ["message"] = commitMessage,
                    ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(change.Content)),
                    ["branch"] = branch
                };
                if (!string.IsNullOrWhiteSpace(currentSha))
                {
                    payload["sha"] = currentSha;
                }

                using var putReq = CreateRequest(HttpMethod.Put, $"{apiBase}/repos/{repoFullName}/contents/{targetPath}", account.Token);
                putReq.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

                using var putRes = await _httpClient.SendAsync(putReq, cancellationToken);
                if (putRes.IsSuccessStatusCode)
                {
                    var resJson = await putRes.Content.ReadAsStringAsync(cancellationToken);
                    var commitNode = JsonNode.Parse(resJson)?["commit"];
                    return new GitCommitResult
                    {
                        Sha = commitNode?["sha"]?.ToString() ?? "",
                        Message = commitMessage,
                        Author = account.Username,
                        HtmlUrl = commitNode?["html_url"]?.ToString()
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitHubProvider] Commit error: {ex.Message}");
        }
        return new GitCommitResult { Message = commitMessage };
    }

    public async Task<GitPullRequest> CreatePullRequestAsync(GitAccount account, string repoFullName, string title, string headBranch, string baseBranch, string description, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            var payload = new JsonObject
            {
                ["title"] = title,
                ["head"] = headBranch,
                ["base"] = baseBranch,
                ["body"] = description
            };

            using var req = CreateRequest(HttpMethod.Post, $"{apiBase}/repos/{repoFullName}/pulls", account.Token);
            req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(cancellationToken);
                var node = JsonNode.Parse(json);
                return new GitPullRequest
                {
                    Id = node?["id"]?.ToString() ?? "",
                    Number = node?["number"]?.GetValue<int>() ?? 0,
                    Title = title,
                    Description = description,
                    HeadBranch = headBranch,
                    BaseBranch = baseBranch,
                    State = node?["state"]?.ToString() ?? "open",
                    HtmlUrl = node?["html_url"]?.ToString() ?? ""
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitHubProvider] PR error: {ex.Message}");
        }
        return new GitPullRequest { Title = title };
    }
}

public sealed class GitLabProvider : IGitProvider
{
    private readonly HttpClient _httpClient;

    public GitProviderType ProviderType => GitProviderType.GitLab;
    public string Name => "GitLab";
    public string DefaultInstanceUrl => "https://gitlab.com";

    public GitLabProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Gram-Blazor", "1.0"));
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Add("PRIVATE-TOKEN", token);
        }
        return req;
    }

    private string GetApiBase(string instanceUrl)
    {
        var trimmed = string.IsNullOrWhiteSpace(instanceUrl) ? "https://gitlab.com" : instanceUrl.TrimEnd('/');
        return $"{trimmed}/api/v4";
    }

    public async Task<bool> ValidateCredentialsAsync(GitAccount account, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/user", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<GitRepository>> ListRepositoriesAsync(GitAccount account, CancellationToken cancellationToken = default)
    {
        var list = new List<GitRepository>();
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/projects?membership=true&per_page=100&order_by=updated_at", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                list.Add(new GitRepository
                {
                    Id = item["id"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    FullName = item["path_with_namespace"]?.ToString() ?? item["name"]?.ToString() ?? "",
                    Description = item["description"]?.ToString() ?? "",
                    DefaultBranch = item["default_branch"]?.ToString() ?? "main",
                    IsPrivate = item["visibility"]?.ToString() == "private",
                    CloneUrl = item["http_url_to_repo"]?.ToString() ?? "",
                    HtmlUrl = item["web_url"]?.ToString() ?? "",
                    Owner = item["namespace"]?["name"]?.ToString() ?? ""
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitLabProvider] Error listing projects: {ex.Message}");
        }
        return list;
    }

    public async Task<List<GitBranch>> ListBranchesAsync(GitAccount account, string repoFullName, CancellationToken cancellationToken = default)
    {
        var list = new List<GitBranch>();
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            var encodedId = Uri.EscapeDataString(repoFullName);
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/projects/{encodedId}/repository/branches?per_page=100", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                list.Add(new GitBranch
                {
                    Name = item["name"]?.ToString() ?? "",
                    CommitSha = item["commit"]?["id"]?.ToString() ?? "",
                    IsDefault = item["default"]?.GetValue<bool>() ?? false
                });
            }
        }
        catch {}
        return list;
    }

    public async Task<string> GetFileContentAsync(GitAccount account, string repoFullName, string branch, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            var encodedId = Uri.EscapeDataString(repoFullName);
            var encodedPath = Uri.EscapeDataString(filePath.TrimStart('/'));
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/projects/{encodedId}/repository/files/{encodedPath}/raw?ref={branch}", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return $"Error fetching file from GitLab: {ex.Message}";
        }
        return "";
    }

    public async Task<List<GitFileItem>> ListDirectoryAsync(GitAccount account, string repoFullName, string branch, string path = "", CancellationToken cancellationToken = default)
    {
        var list = new List<GitFileItem>();
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            var encodedId = Uri.EscapeDataString(repoFullName);
            var query = $"?ref={branch}&per_page=100" + (string.IsNullOrWhiteSpace(path) ? "" : $"&path={Uri.EscapeDataString(path.TrimStart('/'))}");
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/projects/{encodedId}/repository/tree{query}", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                var typeStr = item["type"]?.ToString() ?? "blob";
                list.Add(new GitFileItem
                {
                    Path = item["path"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    Type = typeStr == "tree" ? "directory" : "file",
                    Sha = item["id"]?.ToString() ?? ""
                });
            }
        }
        catch {}
        return list;
    }

    public async Task<GitCommitResult> CommitChangesAsync(GitAccount account, string repoFullName, string branch, string commitMessage, List<FileChange> changes, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            var encodedId = Uri.EscapeDataString(repoFullName);

            var actions = new JsonArray();
            foreach (var ch in changes)
            {
                actions.Add(new JsonObject
                {
                    ["action"] = ch.Action == "delete" ? "delete" : (ch.Action == "create" ? "create" : "update"),
                    ["file_path"] = ch.Path.TrimStart('/'),
                    ["content"] = ch.Content
                });
            }

            var payload = new JsonObject
            {
                ["branch"] = branch,
                ["commit_message"] = commitMessage,
                ["actions"] = actions
            };

            using var req = CreateRequest(HttpMethod.Post, $"{apiBase}/projects/{encodedId}/repository/commits", account.Token);
            req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(cancellationToken);
                var node = JsonNode.Parse(json);
                return new GitCommitResult
                {
                    Sha = node?["id"]?.ToString() ?? "",
                    Message = commitMessage,
                    Author = node?["author_name"]?.ToString() ?? account.Username,
                    HtmlUrl = node?["web_url"]?.ToString()
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitLabProvider] Commit error: {ex.Message}");
        }
        return new GitCommitResult { Message = commitMessage };
    }

    public async Task<GitPullRequest> CreatePullRequestAsync(GitAccount account, string repoFullName, string title, string headBranch, string baseBranch, string description, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            var encodedId = Uri.EscapeDataString(repoFullName);
            var payload = new JsonObject
            {
                ["title"] = title,
                ["source_branch"] = headBranch,
                ["target_branch"] = baseBranch,
                ["description"] = description
            };

            using var req = CreateRequest(HttpMethod.Post, $"{apiBase}/projects/{encodedId}/merge_requests", account.Token);
            req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(cancellationToken);
                var node = JsonNode.Parse(json);
                return new GitPullRequest
                {
                    Id = node?["id"]?.ToString() ?? "",
                    Number = node?["iid"]?.GetValue<int>() ?? 0,
                    Title = title,
                    Description = description,
                    HeadBranch = headBranch,
                    BaseBranch = baseBranch,
                    State = node?["state"]?.ToString() ?? "opened",
                    HtmlUrl = node?["web_url"]?.ToString() ?? ""
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitLabProvider] MR error: {ex.Message}");
        }
        return new GitPullRequest { Title = title };
    }
}

public sealed class ForgejoProvider : IGitProvider
{
    private readonly HttpClient _httpClient;

    public GitProviderType ProviderType => GitProviderType.Forgejo;
    public string Name => "Forgejo / Gitea";
    public string DefaultInstanceUrl => "https://codeberg.org";

    public ForgejoProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Gram-Blazor", "1.0"));
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("token", token);
        }
        return req;
    }

    private string GetApiBase(string instanceUrl)
    {
        var trimmed = string.IsNullOrWhiteSpace(instanceUrl) ? "https://codeberg.org" : instanceUrl.TrimEnd('/');
        return $"{trimmed}/api/v1";
    }

    public async Task<bool> ValidateCredentialsAsync(GitAccount account, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/user", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<GitRepository>> ListRepositoriesAsync(GitAccount account, CancellationToken cancellationToken = default)
    {
        var list = new List<GitRepository>();
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/user/repos?limit=100", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                list.Add(new GitRepository
                {
                    Id = item["id"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    FullName = item["full_name"]?.ToString() ?? "",
                    Description = item["description"]?.ToString() ?? "",
                    DefaultBranch = item["default_branch"]?.ToString() ?? "main",
                    IsPrivate = item["private"]?.GetValue<bool>() ?? false,
                    CloneUrl = item["clone_url"]?.ToString() ?? "",
                    HtmlUrl = item["html_url"]?.ToString() ?? "",
                    Owner = item["owner"]?["username"]?.ToString() ?? ""
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ForgejoProvider] Error listing repos: {ex.Message}");
        }
        return list;
    }

    public async Task<List<GitBranch>> ListBranchesAsync(GitAccount account, string repoFullName, CancellationToken cancellationToken = default)
    {
        var list = new List<GitBranch>();
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/repos/{repoFullName}/branches", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                list.Add(new GitBranch
                {
                    Name = item["name"]?.ToString() ?? "",
                    CommitSha = item["commit"]?["id"]?.ToString() ?? "",
                    IsDefault = false
                });
            }
        }
        catch {}
        return list;
    }

    public async Task<string> GetFileContentAsync(GitAccount account, string repoFullName, string branch, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/repos/{repoFullName}/raw/{filePath.TrimStart('/')}?ref={branch}", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return $"Error fetching file from Forgejo: {ex.Message}";
        }
        return "";
    }

    public async Task<List<GitFileItem>> ListDirectoryAsync(GitAccount account, string repoFullName, string branch, string path = "", CancellationToken cancellationToken = default)
    {
        var list = new List<GitFileItem>();
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            var targetPath = string.IsNullOrWhiteSpace(path) ? "" : $"/{path.TrimStart('/')}";
            using var req = CreateRequest(HttpMethod.Get, $"{apiBase}/repos/{repoFullName}/contents{targetPath}?ref={branch}", account.Token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                var typeStr = item["type"]?.ToString() ?? "file";
                list.Add(new GitFileItem
                {
                    Path = item["path"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    Type = typeStr == "dir" ? "directory" : "file",
                    Size = item["size"]?.GetValue<long>() ?? 0,
                    Sha = item["sha"]?.ToString() ?? ""
                });
            }
        }
        catch {}
        return list;
    }

    public async Task<GitCommitResult> CommitChangesAsync(GitAccount account, string repoFullName, string branch, string commitMessage, List<FileChange> changes, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            foreach (var change in changes)
            {
                var targetPath = change.Path.TrimStart('/');
                string? currentSha = null;
                try
                {
                    using var checkReq = CreateRequest(HttpMethod.Get, $"{apiBase}/repos/{repoFullName}/contents/{targetPath}?ref={branch}", account.Token);
                    using var checkRes = await _httpClient.SendAsync(checkReq, cancellationToken);
                    if (checkRes.IsSuccessStatusCode)
                    {
                        var checkJson = await checkRes.Content.ReadAsStringAsync(cancellationToken);
                        currentSha = JsonNode.Parse(checkJson)?["sha"]?.ToString();
                    }
                }
                catch {}

                var payload = new JsonObject
                {
                    ["message"] = commitMessage,
                    ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(change.Content)),
                    ["branch"] = branch
                };
                if (!string.IsNullOrWhiteSpace(currentSha))
                {
                    payload["sha"] = currentSha;
                }

                using var putReq = CreateRequest(HttpMethod.Put, $"{apiBase}/repos/{repoFullName}/contents/{targetPath}", account.Token);
                putReq.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

                using var putRes = await _httpClient.SendAsync(putReq, cancellationToken);
                if (putRes.IsSuccessStatusCode)
                {
                    var resJson = await putRes.Content.ReadAsStringAsync(cancellationToken);
                    var commitNode = JsonNode.Parse(resJson)?["commit"];
                    return new GitCommitResult
                    {
                        Sha = commitNode?["sha"]?.ToString() ?? "",
                        Message = commitMessage,
                        Author = account.Username,
                        HtmlUrl = commitNode?["html_url"]?.ToString()
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ForgejoProvider] Commit error: {ex.Message}");
        }
        return new GitCommitResult { Message = commitMessage };
    }

    public async Task<GitPullRequest> CreatePullRequestAsync(GitAccount account, string repoFullName, string title, string headBranch, string baseBranch, string description, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiBase = GetApiBase(account.InstanceUrl);
            var payload = new JsonObject
            {
                ["title"] = title,
                ["head"] = headBranch,
                ["base"] = baseBranch,
                ["body"] = description
            };

            using var req = CreateRequest(HttpMethod.Post, $"{apiBase}/repos/{repoFullName}/pulls", account.Token);
            req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(cancellationToken);
                var node = JsonNode.Parse(json);
                return new GitPullRequest
                {
                    Id = node?["id"]?.ToString() ?? "",
                    Number = node?["number"]?.GetValue<int>() ?? 0,
                    Title = title,
                    Description = description,
                    HeadBranch = headBranch,
                    BaseBranch = baseBranch,
                    State = node?["state"]?.ToString() ?? "open",
                    HtmlUrl = node?["html_url"]?.ToString() ?? ""
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ForgejoProvider] PR error: {ex.Message}");
        }
        return new GitPullRequest { Title = title };
    }
}

public sealed class BitbucketProvider : IGitProvider
{
    private readonly HttpClient _httpClient;

    public GitProviderType ProviderType => GitProviderType.Bitbucket;
    public string Name => "Bitbucket";
    public string DefaultInstanceUrl => "https://bitbucket.org";

    public BitbucketProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string token, string username = "")
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Gram-Blazor", "1.0"));
        if (!string.IsNullOrWhiteSpace(token))
        {
            if (!string.IsNullOrWhiteSpace(username))
            {
                var basicBytes = Encoding.ASCII.GetBytes($"{username}:{token}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(basicBytes));
            }
            else
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }
        return req;
    }

    public async Task<bool> ValidateCredentialsAsync(GitAccount account, CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = CreateRequest(HttpMethod.Get, "https://api.bitbucket.org/2.0/user", account.Token, account.Username);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<GitRepository>> ListRepositoriesAsync(GitAccount account, CancellationToken cancellationToken = default)
    {
        var list = new List<GitRepository>();
        try
        {
            using var req = CreateRequest(HttpMethod.Get, "https://api.bitbucket.org/2.0/repositories?role=member&pagelen=100", account.Token, account.Username);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?["values"]?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                list.Add(new GitRepository
                {
                    Id = item["uuid"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    FullName = item["full_name"]?.ToString() ?? "",
                    Description = item["description"]?.ToString() ?? "",
                    DefaultBranch = item["mainbranch"]?["name"]?.ToString() ?? "main",
                    IsPrivate = item["is_private"]?.GetValue<bool>() ?? false,
                    CloneUrl = item["links"]?["clone"]?[0]?["href"]?.ToString() ?? "",
                    HtmlUrl = item["links"]?["html"]?["href"]?.ToString() ?? "",
                    Owner = item["owner"]?["display_name"]?.ToString() ?? ""
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BitbucketProvider] Error listing repos: {ex.Message}");
        }
        return list;
    }

    public async Task<List<GitBranch>> ListBranchesAsync(GitAccount account, string repoFullName, CancellationToken cancellationToken = default)
    {
        var list = new List<GitBranch>();
        try
        {
            using var req = CreateRequest(HttpMethod.Get, $"https://api.bitbucket.org/2.0/repositories/{repoFullName}/refs/branches?pagelen=100", account.Token, account.Username);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?["values"]?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                list.Add(new GitBranch
                {
                    Name = item["name"]?.ToString() ?? "",
                    CommitSha = item["target"]?["hash"]?.ToString() ?? "",
                    IsDefault = false
                });
            }
        }
        catch {}
        return list;
    }

    public async Task<string> GetFileContentAsync(GitAccount account, string repoFullName, string branch, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = CreateRequest(HttpMethod.Get, $"https://api.bitbucket.org/2.0/repositories/{repoFullName}/src/{branch}/{filePath.TrimStart('/')}", account.Token, account.Username);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return $"Error fetching file from Bitbucket: {ex.Message}";
        }
        return "";
    }

    public async Task<List<GitFileItem>> ListDirectoryAsync(GitAccount account, string repoFullName, string branch, string path = "", CancellationToken cancellationToken = default)
    {
        var list = new List<GitFileItem>();
        try
        {
            var targetPath = string.IsNullOrWhiteSpace(path) ? "" : $"{path.TrimStart('/')}/";
            using var req = CreateRequest(HttpMethod.Get, $"https://api.bitbucket.org/2.0/repositories/{repoFullName}/src/{branch}/{targetPath}?pagelen=100", account.Token, account.Username);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            var arr = JsonNode.Parse(json)?["values"]?.AsArray();
            if (arr == null) return list;

            foreach (var item in arr)
            {
                if (item == null) continue;
                var typeStr = item["type"]?.ToString() ?? "commit_file";
                list.Add(new GitFileItem
                {
                    Path = item["path"]?.ToString() ?? "",
                    Name = item["path"]?.ToString()?.Split('/').LastOrDefault() ?? "",
                    Type = typeStr == "commit_directory" ? "directory" : "file",
                    Size = item["size"]?.GetValue<long>() ?? 0
                });
            }
        }
        catch {}
        return list;
    }

    public async Task<GitCommitResult> CommitChangesAsync(GitAccount account, string repoFullName, string branch, string commitMessage, List<FileChange> changes, CancellationToken cancellationToken = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(commitMessage), "message");
            form.Add(new StringContent(branch), "branch");
            form.Add(new StringContent(account.Username), "author");

            foreach (var change in changes)
            {
                var content = new StringContent(change.Content, Encoding.UTF8);
                form.Add(content, change.Path.TrimStart('/'));
            }

            using var req = CreateRequest(HttpMethod.Post, $"https://api.bitbucket.org/2.0/repositories/{repoFullName}/src", account.Token, account.Username);
            req.Content = form;

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                return new GitCommitResult
                {
                    Message = commitMessage,
                    Author = account.Username,
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BitbucketProvider] Commit error: {ex.Message}");
        }
        return new GitCommitResult { Message = commitMessage };
    }

    public async Task<GitPullRequest> CreatePullRequestAsync(GitAccount account, string repoFullName, string title, string headBranch, string baseBranch, string description, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new JsonObject
            {
                ["title"] = title,
                ["description"] = description,
                ["source"] = new JsonObject { ["branch"] = new JsonObject { ["name"] = headBranch } },
                ["destination"] = new JsonObject { ["branch"] = new JsonObject { ["name"] = baseBranch } }
            };

            using var req = CreateRequest(HttpMethod.Post, $"https://api.bitbucket.org/2.0/repositories/{repoFullName}/pullrequests", account.Token, account.Username);
            req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(cancellationToken);
                var node = JsonNode.Parse(json);
                return new GitPullRequest
                {
                    Id = node?["id"]?.ToString() ?? "",
                    Number = node?["id"]?.GetValue<int>() ?? 0,
                    Title = title,
                    Description = description,
                    HeadBranch = headBranch,
                    BaseBranch = baseBranch,
                    State = node?["state"]?.ToString() ?? "OPEN",
                    HtmlUrl = node?["links"]?["html"]?["href"]?.ToString() ?? ""
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BitbucketProvider] PR error: {ex.Message}");
        }
        return new GitPullRequest { Title = title };
    }
}
