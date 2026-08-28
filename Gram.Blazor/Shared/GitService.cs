using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class GitService
{
    private readonly StorageService _storage;
    private readonly string _filePath;
    private readonly Dictionary<GitProviderType, IGitProvider> _providers = new();

    public List<GitAccount> Accounts { get; private set; } = new();
    public event Action? OnChange;

    public GitService(StorageService storage, HttpClient? httpClient = null)
    {
        _storage = storage;
        _filePath = _storage.GetFilePath("git_accounts.json");

        var client = httpClient ?? new HttpClient();
        RegisterProvider(new GitHubProvider(client));
        RegisterProvider(new GitLabProvider(client));
        RegisterProvider(new ForgejoProvider(client));
        RegisterProvider(new BitbucketProvider(client));

        _ = LoadAccountsAsync();
    }

    public void RegisterProvider(IGitProvider provider)
    {
        _providers[provider.ProviderType] = provider;
    }

    public IGitProvider? GetProvider(GitProviderType type)
    {
        _providers.TryGetValue(type, out var p);
        return p;
    }

    public IEnumerable<IGitProvider> GetAllProviders() => _providers.Values;

    public async Task LoadAccountsAsync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                var loaded = JsonSerializer.Deserialize<List<GitAccount>>(json);
                if (loaded != null)
                {
                    Accounts = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitService] Error loading accounts: {ex.Message}");
        }
        OnChange?.Invoke();
    }

    public async Task SaveAccountsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(Accounts, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitService] Error saving accounts: {ex.Message}");
        }
        OnChange?.Invoke();
    }

    public async Task<bool> SaveAccountAsync(GitAccount account)
    {
        var existing = Accounts.FirstOrDefault(a => a.Id == account.Id);
        if (existing != null)
        {
            existing.DisplayName = account.DisplayName;
            existing.ProviderType = account.ProviderType;
            existing.InstanceUrl = account.InstanceUrl;
            existing.Username = account.Username;
            existing.Token = account.Token;
            existing.IsDefault = account.IsDefault;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            if (Accounts.Count == 0) account.IsDefault = true;
            Accounts.Add(account);
        }

        if (account.IsDefault)
        {
            foreach (var a in Accounts.Where(a => a.Id != account.Id))
            {
                a.IsDefault = false;
            }
        }

        await SaveAccountsAsync();
        return true;
    }

    public async Task DeleteAccountAsync(string accountId)
    {
        var acc = Accounts.FirstOrDefault(a => a.Id == accountId);
        if (acc != null)
        {
            Accounts.Remove(acc);
            if (acc.IsDefault && Accounts.Count > 0)
            {
                Accounts[0].IsDefault = true;
            }
            await SaveAccountsAsync();
        }
    }

    public async Task<bool> TestConnectionAsync(GitAccount account)
    {
        var provider = GetProvider(account.ProviderType);
        if (provider == null) return false;
        return await provider.ValidateCredentialsAsync(account);
    }

    public async Task<GitUserProfile> ValidateAndFetchProfileAsync(GitAccount account, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(account.ProviderType);
        if (provider == null)
        {
            return new GitUserProfile { Success = false, ErrorMessage = $"Provider {account.ProviderType} is not supported." };
        }
        return await provider.GetUserProfileAsync(account, cancellationToken);
    }
}
