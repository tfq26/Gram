using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public enum ProviderType
{
    OpenRouter,
    OpenAI,
    Anthropic,
    Google,
    Ollama,
    xAI,
    CustomOpenAI,
    CustomAnthropic
}

public sealed class GramModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "OpenRouter";
    public string? Description { get; set; }
    public int? ContextLength { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProviderConfig
{
    public string Id { get; set; } = "";
    public ProviderType Type { get; set; }
    public string Name { get; set; } = "";
    public string? BaseUrl { get; set; }
    public bool IsConnected { get; set; }
    public bool IsSystemManaged { get; set; }
    public DateTimeOffset? LastTested { get; set; }
}

public sealed class DiscoveredModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? ContextLength { get; set; }
    public string Provider { get; set; } = "OpenRouter";
    public bool IsSelected { get; set; }
    public bool IsFavorite { get; set; }
}

public sealed class ModelService
{
    public event Action? OnChange;

    private readonly string storageDir;
    private readonly string modelsJsonPath;
    private readonly string providersJsonPath;
    private readonly string secureKeysPath;
    private readonly HttpClient httpClient = new();

    public List<GramModel> Models { get; private set; } = new();
    public List<ProviderConfig> Providers { get; private set; } = new();
    private readonly Dictionary<string, string> secureKeys = new(StringComparer.OrdinalIgnoreCase); // User BYOK Keys
    private readonly Dictionary<string, string> systemKeys = new(StringComparer.OrdinalIgnoreCase); // Internal System Keys

    public ModelService(StorageService? storageService = null)
    {
        storageService ??= new StorageService();
        storageDir = storageService.DataDirectory;
        modelsJsonPath = storageService.GetFilePath("models.json");
        providersJsonPath = storageService.GetFilePath("providers.json");
        secureKeysPath = storageService.GetFilePath("secure_keys.dat");

        storageService.EnsureDirectories();
        LoadProviders();
        LoadSecureKeys();
        LoadSystemKeys();
        LoadModels();
    }

    private void LoadProviders()
    {
        try
        {
            if (File.Exists(providersJsonPath))
            {
                var json = File.ReadAllText(providersJsonPath);
                var list = JsonSerializer.Deserialize<List<ProviderConfig>>(json);
                if (list != null && list.Count > 0)
                {
                    Providers = list;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModelService] Error loading providers: {ex.Message}");
        }

        // Initialize default supported providers
        Providers =
        [
            new ProviderConfig { Id = "groq", Type = ProviderType.OpenAI, Name = "Groq", BaseUrl = "https://api.groq.com/openai/v1" },
            new ProviderConfig { Id = "google", Type = ProviderType.Google, Name = "Google Gemini", BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai" },
            new ProviderConfig { Id = "cloudflare", Type = ProviderType.CustomOpenAI, Name = "Cloudflare Workers AI", BaseUrl = "https://api.cloudflare.com/client/v4/accounts/{account_id}/ai/v1" },
            new ProviderConfig { Id = "openai", Type = ProviderType.OpenAI, Name = "OpenAI", BaseUrl = "https://api.openai.com/v1" },
            new ProviderConfig { Id = "anthropic", Type = ProviderType.Anthropic, Name = "Anthropic", BaseUrl = "https://api.anthropic.com/v1" },
            new ProviderConfig { Id = "openrouter", Type = ProviderType.OpenRouter, Name = "OpenRouter", BaseUrl = "https://openrouter.ai/api/v1" },
            new ProviderConfig { Id = "xai", Type = ProviderType.xAI, Name = "xAI (Grok)", BaseUrl = "https://api.x.ai/v1" },
            new ProviderConfig { Id = "deepseek", Type = ProviderType.CustomOpenAI, Name = "DeepSeek", BaseUrl = "https://api.deepseek.com/v1" },
            new ProviderConfig { Id = "ollama", Type = ProviderType.Ollama, Name = "Ollama (Local)", BaseUrl = "http://localhost:11434" }
        ];
    }

    private void LoadModels()
    {
        try
        {
            if (File.Exists(modelsJsonPath))
            {
                var json = File.ReadAllText(modelsJsonPath);
                var list = JsonSerializer.Deserialize<List<GramModel>>(json);
                if (list != null && list.Count > 0)
                {
                    // Ensure canonical descriptions for default engines
                    var qModel = list.FirstOrDefault(m => m.Id.Equals("gram-q", StringComparison.OrdinalIgnoreCase));
                    if (qModel != null)
                    {
                        qModel.Name = "Gram Q";
                        qModel.Provider = "Groq";
                        qModel.Description = "Ultra-fast daily intelligence · Powered by GPT OSS 120B on Groq LPU (131K context)";
                        qModel.ContextLength = 131072;
                    }
                    var qPlusModel = list.FirstOrDefault(m => m.Id.Equals("gram-q-plus", StringComparison.OrdinalIgnoreCase));
                    if (qPlusModel != null)
                    {
                        qPlusModel.Name = "Gram Q+";
                        qPlusModel.Provider = "Google";
                        qPlusModel.Description = "Deep-context reasoning flagship · Powered by Google Gemini 3 Flash Preview (1M context)";
                        qPlusModel.ContextLength = 1000000;
                    }

                    Models = list;
                    SaveModels();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModelService] Error loading models: {ex.Message}");
        }

        // Default seed models (Gram Q, Gram Q+, and BYOK models)
        Models =
        [
            new GramModel 
            { 
                Id = "gram-q", 
                Name = "Gram Q", 
                Provider = "Groq", 
                Description = "Ultra-fast daily intelligence · Powered by GPT OSS 120B on Groq LPU (131K context)", 
                ContextLength = 131072, 
                IsFavorite = true 
            },
            new GramModel 
            { 
                Id = "gram-q-plus", 
                Name = "Gram Q+", 
                Provider = "Google", 
                Description = "Deep-context reasoning flagship · Powered by Google Gemini 3 Flash Preview (1M context)", 
                ContextLength = 1000000, 
                IsFavorite = true 
            },
            new GramModel 
            { 
                Id = "anthropic/claude-3.7-sonnet", 
                Name = "Claude 3.7 Sonnet (BYOK)", 
                Provider = "Anthropic", 
                Description = "Requires Anthropic API Key in Settings", 
                ContextLength = 200000, 
                IsFavorite = false 
            },
            new GramModel 
            { 
                Id = "openai/gpt-4o", 
                Name = "GPT-4o (BYOK)", 
                Provider = "OpenAI", 
                Description = "Requires OpenAI API Key in Settings", 
                ContextLength = 128000, 
                IsFavorite = false 
            }
        ];
    }

    private void LoadSecureKeys()
    {
        try
        {
            if (File.Exists(secureKeysPath))
            {
                var bytes = File.ReadAllBytes(secureKeysPath);
                var json = Decrypt(bytes);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    foreach (var (k, v) in dict)
                    {
                        if (!string.IsNullOrWhiteSpace(v))
                        {
                            secureKeys[k] = v.Trim();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModelService] Error loading secure keys: {ex.Message}");
        }
    }

    private void LoadSystemKeys()
    {
        systemKeys.Clear();

        // System keys are strictly reserved for Gram Q and Gram Q+
        RegisterSystemKey("groq", "GROQ_API_KEY", "GRAM_GROQ_KEY");
        RegisterSystemKey("google", "GEMINI_API_KEY", "GOOGLE_API_KEY");

        // Fall back to secure key storage if environment variables are not set
        if (!systemKeys.ContainsKey("groq") && secureKeys.TryGetValue("groq", out var groqKey) && !string.IsNullOrWhiteSpace(groqKey))
        {
            systemKeys["groq"] = groqKey;
        }
        if (!systemKeys.ContainsKey("google") && secureKeys.TryGetValue("google", out var googleKey) && !string.IsNullOrWhiteSpace(googleKey))
        {
            systemKeys["google"] = googleKey;
        }

        // Refresh connected states for providers
        foreach (var prov in Providers)
        {
            prov.IsConnected = IsConfigured(prov.Id);
            prov.IsSystemManaged = IsSystemManaged(prov.Id);
        }
    }

    private void RegisterSystemKey(string providerId, params string[] envVarNames)
    {
        foreach (var envVar in envVarNames)
        {
            var key = Environment.GetEnvironmentVariable(envVar, EnvironmentVariableTarget.Process)
                      ?? (OperatingSystem.IsWindows()
                          ? Environment.GetEnvironmentVariable(envVar, EnvironmentVariableTarget.User)
                            ?? Environment.GetEnvironmentVariable(envVar, EnvironmentVariableTarget.Machine)
                          : null);

            if (!string.IsNullOrWhiteSpace(key))
            {
                systemKeys[providerId] = key.Trim();
                break;
            }
        }
    }

    public bool HasUserKey(string providerId)
    {
        return secureKeys.TryGetValue(providerId, out var key) && !string.IsNullOrWhiteSpace(key);
    }

    public bool HasSystemKey(string providerId)
    {
        return systemKeys.TryGetValue(providerId, out var key) && !string.IsNullOrWhiteSpace(key);
    }

    public bool IsConfigured(string providerId)
    {
        return HasUserKey(providerId) || HasSystemKey(providerId);
    }

    public bool IsSystemManaged(string providerId)
    {
        return !HasUserKey(providerId) && HasSystemKey(providerId);
    }

    /// <summary>
    /// Returns the active API key for backend execution.
    /// User BYOK keys are used for any provider. System keys are ONLY allowed for groq and google.
    /// </summary>
    public string? GetApiKey(string providerId)
    {
        var normId = providerId.ToLowerInvariant();

        // 1. User BYOK key always takes top priority
        if (secureKeys.TryGetValue(normId, out var userKey) && !string.IsNullOrWhiteSpace(userKey))
        {
            return userKey;
        }

        // 2. System keys are strictly scoped to groq and google (for Gram Q / Q+)
        if ((normId == "groq" || normId == "google") && systemKeys.TryGetValue(normId, out var sysKey) && !string.IsNullOrWhiteSpace(sysKey))
        {
            return sysKey;
        }

        return null;
    }

    /// <summary>
    /// Returns ONLY the user-provided BYOK key. NEVER exposes internal system keys to the UI.
    /// </summary>
    public string? GetUserApiKey(string providerId)
    {
        return secureKeys.TryGetValue(providerId, out var key) && !string.IsNullOrWhiteSpace(key) ? key : null;
    }

    /// <summary>
    /// Returns internal system key (strictly reserved for Gram Q and Gram Q+ backend execution).
    /// </summary>
    public string? GetSystemApiKey(string providerId)
    {
        return systemKeys.TryGetValue(providerId, out var key) && !string.IsNullOrWhiteSpace(key) ? key : null;
    }

    /// <summary>
    /// Returns a masked representation for UI presentation.
    /// </summary>
    public string GetMaskedApiKey(string providerId)
    {
        if (secureKeys.TryGetValue(providerId, out var key) && !string.IsNullOrWhiteSpace(key))
        {
            if (key.Length > 8)
            {
                return key[..4] + "••••••••" + key[^4..];
            }
            return "••••••••";
        }

        if (HasSystemKey(providerId))
        {
            return "System Managed (Protected)";
        }

        return "";
    }

    public void SetApiKey(string providerId, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            secureKeys.Remove(providerId);
        }
        else
        {
            secureKeys[providerId] = apiKey.Trim();
        }

        SaveSecureKeys();

        var prov = Providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (prov != null)
        {
            prov.IsConnected = IsConfigured(providerId);
            prov.IsSystemManaged = IsSystemManaged(providerId);
            SaveProviders();
        }

        OnChange?.Invoke();
    }

    private void SaveSecureKeys()
    {
        try
        {
            var json = JsonSerializer.Serialize(secureKeys);
            var bytes = Encrypt(json);
            File.WriteAllBytes(secureKeysPath, bytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModelService] Error saving secure keys: {ex.Message}");
        }
    }

    private static byte[] Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + Environment.MachineName + "GramSecretKeySalt2026"));
        aes.Key = key;
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plainText);
        }
        return ms.ToArray();
    }

    private static string Decrypt(byte[] cipherBytes)
    {
        if (cipherBytes.Length < 16) return Encoding.UTF8.GetString(cipherBytes);
        try
        {
            using var aes = Aes.Create();
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + Environment.MachineName + "GramSecretKeySalt2026"));
            aes.Key = key;

            var iv = new byte[16];
            Array.Copy(cipherBytes, 0, iv, 0, 16);
            aes.IV = iv;

            using var ms = new MemoryStream(cipherBytes, 16, cipherBytes.Length - 16);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            return sr.ReadToEnd();
        }
        catch
        {
            return Encoding.UTF8.GetString(cipherBytes);
        }
    }

    public void UpdateProvider(string providerId, string? baseUrl, string? apiKey)
    {
        var prov = Providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (prov != null)
        {
            if (baseUrl != null)
            {
                prov.BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim();
            }
            SaveProviders();
        }

        if (apiKey != null)
        {
            SetApiKey(providerId, apiKey);
        }
        else
        {
            OnChange?.Invoke();
        }
    }

    public void SaveProviders()
    {
        try
        {
            var json = JsonSerializer.Serialize(Providers, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(providersJsonPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModelService] Error saving providers: {ex.Message}");
        }
    }

    public void SaveModels()
    {
        try
        {
            var json = JsonSerializer.Serialize(Models, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(modelsJsonPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModelService] Error saving models: {ex.Message}");
        }
        OnChange?.Invoke();
    }

    public void ToggleFavorite(string modelId)
    {
        var model = Models.FirstOrDefault(m => m.Id == modelId);
        if (model != null)
        {
            model.IsFavorite = !model.IsFavorite;
            SaveModels();
        }
    }

    public void RemoveModel(string modelId)
    {
        Models.RemoveAll(m => m.Id == modelId);
        SaveModels();
    }

    public void AddModels(IEnumerable<DiscoveredModel> selected)
    {
        foreach (var item in selected)
        {
            if (!Models.Any(m => m.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)))
            {
                Models.Add(new GramModel
                {
                    Id = item.Id,
                    Name = item.Name,
                    Provider = item.Provider,
                    Description = item.Description,
                    ContextLength = item.ContextLength,
                    IsFavorite = item.IsFavorite,
                    IsEnabled = true
                });
            }
        }
        SaveModels();
    }

    public async Task<List<DiscoveredModel>> DiscoverModelsAsync(string? providerFilter = null)
    {
        var allDiscovered = new List<DiscoveredModel>();

        // 1. Anthropic Flagship Models
        if (string.IsNullOrWhiteSpace(providerFilter) || providerFilter.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            allDiscovered.AddRange(
            [
                new() { Id = "anthropic/claude-3.7-sonnet", Name = "Claude 3.7 Sonnet", Provider = "Anthropic", ContextLength = 200000, Description = "Hybrid reasoning and coding flagship by Anthropic" },
                new() { Id = "anthropic/claude-3.5-sonnet", Name = "Claude 3.5 Sonnet", Provider = "Anthropic", ContextLength = 200000, Description = "Frontier agentic and coding intelligence" },
                new() { Id = "anthropic/claude-3.5-haiku", Name = "Claude 3.5 Haiku", Provider = "Anthropic", ContextLength = 200000, Description = "High-speed compact agentic model" },
                new() { Id = "anthropic/claude-3-opus", Name = "Claude 3 Opus", Provider = "Anthropic", ContextLength = 200000, Description = "Deep analytical and architectural reasoning" }
            ]);
        }

        // 2. OpenAI Flagship Models
        if (string.IsNullOrWhiteSpace(providerFilter) || providerFilter.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            allDiscovered.AddRange(
            [
                new() { Id = "openai/gpt-4.5-preview", Name = "GPT-4.5 Preview", Provider = "OpenAI", ContextLength = 128000, Description = "OpenAI's largest knowledge and reasoning model" },
                new() { Id = "openai/gpt-4o", Name = "GPT-4o", Provider = "OpenAI", ContextLength = 128000, Description = "Versatile flagship multimodal intelligence" },
                new() { Id = "openai/gpt-4o-mini", Name = "GPT-4o Mini", Provider = "OpenAI", ContextLength = 128000, Description = "Affordable, fast, lightweight model" },
                new() { Id = "openai/o1", Name = "OpenAI o1", Provider = "OpenAI", ContextLength = 200000, Description = "Deep reasoning and scientific problem solving" },
                new() { Id = "openai/o3-mini", Name = "OpenAI o3-mini", Provider = "OpenAI", ContextLength = 200000, Description = "High-speed coding and STEM reasoning" }
            ]);
        }

        // 3. Google Gemini Models
        if (string.IsNullOrWhiteSpace(providerFilter) || providerFilter.Equals("Google", StringComparison.OrdinalIgnoreCase))
        {
            allDiscovered.AddRange(
            [
                new() { Id = "google/gemini-2.5-flash", Name = "Gemini 2.5 Flash", Provider = "Google", ContextLength = 1000000, Description = "Next-gen ultra-fast 1M context flagship" },
                new() { Id = "google/gemini-2.5-pro", Name = "Gemini 2.5 Pro", Provider = "Google", ContextLength = 2000000, Description = "Frontier reasoning and deep analysis with 2M context" },
                new() { Id = "google/gemini-1.5-pro", Name = "Gemini 1.5 Pro", Provider = "Google", ContextLength = 2000000, Description = "Long-context reasoning flagship" },
                new() { Id = "google/gemini-1.5-flash", Name = "Gemini 1.5 Flash", Provider = "Google", ContextLength = 1000000, Description = "Fast, efficient general purpose model" }
            ]);
        }

        // 4. Cloudflare Workers AI Models
        if (string.IsNullOrWhiteSpace(providerFilter) || providerFilter.Equals("Cloudflare", StringComparison.OrdinalIgnoreCase))
        {
            allDiscovered.AddRange(
            [
                new() { Id = "cloudflare/@cf/meta/llama-3.3-70b-instruct", Name = "Llama 3.3 70B Instruct", Provider = "Cloudflare", ContextLength = 128000, Description = "Meta's flagship open model running on Cloudflare Edge" },
                new() { Id = "cloudflare/@cf/deepseek-ai/deepseek-r1-distill-qwen-32b", Name = "DeepSeek R1 Distill 32B", Provider = "Cloudflare", ContextLength = 64000, Description = "High performance reasoning on Cloudflare Edge" },
                new() { Id = "cloudflare/@cf/meta/llama-3.1-8b-instruct", Name = "Llama 3.1 8B Instruct", Provider = "Cloudflare", ContextLength = 128000, Description = "Low latency edge instruction model" },
                new() { Id = "cloudflare/@cf/qwen/qwen2.5-coder-32b-instruct", Name = "Qwen 2.5 Coder 32B", Provider = "Cloudflare", ContextLength = 32000, Description = "Code generation and repair specialist" }
            ]);
        }

        // 5. DeepSeek Models
        if (string.IsNullOrWhiteSpace(providerFilter) || providerFilter.Equals("DeepSeek", StringComparison.OrdinalIgnoreCase))
        {
            allDiscovered.AddRange(
            [
                new() { Id = "deepseek/deepseek-chat", Name = "DeepSeek V3", Provider = "DeepSeek", ContextLength = 64000, Description = "High capability general purpose open-architecture model" },
                new() { Id = "deepseek/deepseek-reasoner", Name = "DeepSeek R1", Provider = "DeepSeek", ContextLength = 64000, Description = "Advanced reasoning and mathematical synthesis" }
            ]);
        }

        // 6. xAI (Grok) Models
        if (string.IsNullOrWhiteSpace(providerFilter) || providerFilter.Equals("xAI", StringComparison.OrdinalIgnoreCase))
        {
            allDiscovered.AddRange(
            [
                new() { Id = "xai/grok-2-1212", Name = "Grok 2", Provider = "xAI", ContextLength = 128000, Description = "State of the art frontier intelligence by xAI" },
                new() { Id = "xai/grok-2-vision-1212", Name = "Grok 2 Vision", Provider = "xAI", ContextLength = 128000, Description = "Multimodal reasoning and vision by xAI" },
                new() { Id = "xai/grok-beta", Name = "Grok Beta", Provider = "xAI", ContextLength = 128000, Description = "Direct knowledge engine by xAI" }
            ]);
        }

        // 7. Ollama Local Models
        if (string.IsNullOrWhiteSpace(providerFilter) || providerFilter.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var ollamaBase = Providers.FirstOrDefault(p => p.Id == "ollama")?.BaseUrl ?? "http://localhost:11434";
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{ollamaBase.TrimEnd('/')}/api/tags");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var res = await httpClient.SendAsync(req, cts.Token);
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("models", out var modelsArr) && modelsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in modelsArr.EnumerateArray())
                        {
                            var modelName = m.GetProperty("name").GetString() ?? "";
                            allDiscovered.Add(new DiscoveredModel
                            {
                                Id = $"ollama/{modelName}",
                                Name = modelName,
                                Provider = "Ollama",
                                ContextLength = 32000,
                                Description = "Local model hosted via Ollama"
                            });
                        }
                    }
                }
            }
            catch {}
        }

        // 8. OpenRouter Live Catalog
        if (string.IsNullOrWhiteSpace(providerFilter) || providerFilter.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
        {
            var openRouterList = await FetchOpenRouterModelsAsync();
            foreach (var orModel in openRouterList)
            {
                if (!allDiscovered.Any(m => m.Id.Equals(orModel.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    allDiscovered.Add(orModel);
                }
            }
        }

        // Set favorite state from currently saved models
        foreach (var dm in allDiscovered)
        {
            dm.IsFavorite = Models.Any(m => m.Id.Equals(dm.Id, StringComparison.OrdinalIgnoreCase) && m.IsFavorite);
        }

        return allDiscovered;
    }

    public async Task<List<DiscoveredModel>> FetchOpenRouterModelsAsync(string? apiKey = null)
    {
        var key = apiKey ?? GetApiKey("openrouter");
        var list = new List<DiscoveredModel>();

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
            if (!string.IsNullOrWhiteSpace(key))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }

            var resp = await httpClient.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var content = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in dataArray.EnumerateArray())
                    {
                        var id = el.GetProperty("id").GetString() ?? "";
                        var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                        var desc = el.TryGetProperty("description", out var d) ? d.GetString() : null;
                        int? ctx = el.TryGetProperty("context_length", out var c) && c.TryGetInt32(out var ci) ? ci : null;

                        list.Add(new DiscoveredModel
                        {
                            Id = id,
                            Name = name,
                            Description = desc,
                            ContextLength = ctx,
                            Provider = "OpenRouter",
                            IsSelected = false,
                            IsFavorite = Models.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && m.IsFavorite)
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModelService] Error fetching OpenRouter models: {ex.Message}");
        }

        // If fetch returns empty (e.g. offline/no key yet), provide representative catalog
        if (list.Count == 0)
        {
            list = GetFallbackOpenRouterModels();
        }

        return list;
    }

    private readonly Dictionary<string, ProviderHealthStatus> healthStatuses = new(StringComparer.OrdinalIgnoreCase);

    public ProviderHealthStatus GetHealth(string providerId)
    {
        return healthStatuses.TryGetValue(providerId, out var h) ? h : new ProviderHealthStatus();
    }

    public async Task<ProviderHealthStatus> CheckProviderHealthAsync(string providerId)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var status = new ProviderHealthStatus { LastChecked = DateTimeOffset.UtcNow };

        try
        {
            var prov = Providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
            var baseUrl = prov?.BaseUrl;

            if (providerId.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
            {
                var key = GetApiKey("openrouter");
                if (string.IsNullOrWhiteSpace(key))
                {
                    status.IsHealthy = false;
                    status.Message = "No API key configured";
                }
                else
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl ?? "https://openrouter.ai/api/v1"}/auth/key");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    var resp = await httpClient.SendAsync(req, cts.Token);
                    sw.Stop();
                    status.LatencyMs = sw.ElapsedMilliseconds;

                    if (resp.IsSuccessStatusCode)
                    {
                        status.IsHealthy = true;
                        status.Message = $"Healthy · {status.LatencyMs}ms";
                    }
                    else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        status.IsHealthy = false;
                        status.Message = "Invalid API key (401)";
                    }
                    else
                    {
                        status.IsHealthy = false;
                        status.Message = $"HTTP {(int)resp.StatusCode}";
                    }
                }
            }
            else if (providerId.Equals("openai", StringComparison.OrdinalIgnoreCase))
            {
                var key = GetApiKey("openai");
                if (string.IsNullOrWhiteSpace(key))
                {
                    status.IsHealthy = false;
                    status.Message = "No API key configured";
                }
                else
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl ?? "https://api.openai.com/v1"}/models");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    var resp = await httpClient.SendAsync(req, cts.Token);
                    sw.Stop();
                    status.LatencyMs = sw.ElapsedMilliseconds;

                    if (resp.IsSuccessStatusCode)
                    {
                        status.IsHealthy = true;
                        status.Message = $"Healthy · {status.LatencyMs}ms";
                    }
                    else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        status.IsHealthy = false;
                        status.Message = "Invalid API key (401)";
                    }
                    else
                    {
                        status.IsHealthy = false;
                        status.Message = $"HTTP {(int)resp.StatusCode}";
                    }
                }
            }
            else if (providerId.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
            {
                var key = GetApiKey("anthropic");
                if (string.IsNullOrWhiteSpace(key))
                {
                    status.IsHealthy = false;
                    status.Message = "No API key configured";
                }
                else
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl ?? "https://api.anthropic.com/v1"}/models");
                    req.Headers.Add("x-api-key", key);
                    req.Headers.Add("anthropic-version", "2023-06-01");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    var resp = await httpClient.SendAsync(req, cts.Token);
                    sw.Stop();
                    status.LatencyMs = sw.ElapsedMilliseconds;

                    if (resp.IsSuccessStatusCode)
                    {
                        status.IsHealthy = true;
                        status.Message = $"Healthy · {status.LatencyMs}ms";
                    }
                    else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        status.IsHealthy = false;
                        status.Message = "Invalid API key";
                    }
                    else
                    {
                        status.IsHealthy = false;
                        status.Message = $"HTTP {(int)resp.StatusCode}";
                    }
                }
            }
            else if (providerId.Equals("google", StringComparison.OrdinalIgnoreCase))
            {
                var key = GetApiKey("google");
                if (string.IsNullOrWhiteSpace(key))
                {
                    status.IsHealthy = false;
                    status.Message = "No API key configured";
                }
                else
                {
                    var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(key)}";
                    using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    var resp = await httpClient.SendAsync(req, cts.Token);
                    sw.Stop();
                    status.LatencyMs = sw.ElapsedMilliseconds;

                    if (resp.IsSuccessStatusCode)
                    {
                        status.IsHealthy = true;
                        status.Message = $"Healthy · {status.LatencyMs}ms";
                    }
                    else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        status.IsHealthy = false;
                        status.Message = "Invalid API key";
                    }
                    else
                    {
                        status.IsHealthy = false;
                        status.Message = $"HTTP {(int)resp.StatusCode}";
                    }
                }
            }
            else if (providerId.Equals("xai", StringComparison.OrdinalIgnoreCase))
            {
                var key = GetApiKey("xai");
                if (string.IsNullOrWhiteSpace(key))
                {
                    status.IsHealthy = false;
                    status.Message = "No API key configured";
                }
                else
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl ?? "https://api.x.ai/v1"}/models");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    var resp = await httpClient.SendAsync(req, cts.Token);
                    sw.Stop();
                    status.LatencyMs = sw.ElapsedMilliseconds;

                    if (resp.IsSuccessStatusCode)
                    {
                        status.IsHealthy = true;
                        status.Message = $"Healthy · {status.LatencyMs}ms";
                    }
                    else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        status.IsHealthy = false;
                        status.Message = "Invalid API key (401)";
                    }
                    else
                    {
                        status.IsHealthy = false;
                        status.Message = $"HTTP {(int)resp.StatusCode}";
                    }
                }
            }
            else if (providerId.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                var ollamaUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434" : baseUrl.TrimEnd('/');
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{ollamaUrl}/api/version");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var resp = await httpClient.SendAsync(req, cts.Token);
                sw.Stop();
                status.LatencyMs = sw.ElapsedMilliseconds;

                if (resp.IsSuccessStatusCode)
                {
                    status.IsHealthy = true;
                    status.Message = $"Running · {status.LatencyMs}ms";
                }
                else
                {
                    status.IsHealthy = false;
                    status.Message = $"Ollama error {(int)resp.StatusCode}";
                }
            }
            else
            {
                status.IsHealthy = false;
                status.Message = "Endpoint not configured";
            }
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            status.IsHealthy = false;
            status.LatencyMs = sw.ElapsedMilliseconds;
            status.Message = "Timed out (>6s)";
        }
        catch (Exception)
        {
            sw.Stop();
            status.IsHealthy = false;
            status.LatencyMs = sw.ElapsedMilliseconds;
            status.Message = "Unreachable";
        }

        healthStatuses[providerId] = status;
        OnChange?.Invoke();
        return status;
    }

    private List<DiscoveredModel> GetFallbackOpenRouterModels()
    {
        return
        [
            new() { Id = "anthropic/claude-3.7-sonnet", Name = "Claude 3.7 Sonnet", ContextLength = 200000, Description = "Hybrid reasoning & coding flagship" },
            new() { Id = "anthropic/claude-3.5-sonnet", Name = "Claude 3.5 Sonnet", ContextLength = 200000, Description = "Fast, accurate agentic intelligence" },
            new() { Id = "anthropic/claude-3.5-haiku", Name = "Claude 3.5 Haiku", ContextLength = 200000, Description = "High-speed compact model" },
            new() { Id = "openai/gpt-4o", Name = "GPT-4o", ContextLength = 128000, Description = "Flagship versatile multimodal model" },
            new() { Id = "openai/gpt-4o-mini", Name = "GPT-4o Mini", ContextLength = 128000, Description = "Lightweight fast multimodal model" },
            new() { Id = "openai/o3-mini", Name = "OpenAI o3-mini", ContextLength = 200000, Description = "High intelligence STEM and coding model" },
            new() { Id = "deepseek/deepseek-r1", Name = "DeepSeek R1", ContextLength = 64000, Description = "Open-weights reasoning model" },
            new() { Id = "deepseek/deepseek-chat", Name = "DeepSeek V3", ContextLength = 64000, Description = "Versatile high performance model" },
            new() { Id = "google/gemini-2.5-flash", Name = "Gemini 2.5 Flash", ContextLength = 1000000, Description = "Ultra-fast large context model" },
            new() { Id = "google/gemini-2.5-pro", Name = "Gemini 2.5 Pro", ContextLength = 2000000, Description = "Complex reasoning & deep analysis" },
            new() { Id = "meta-llama/llama-3.3-70b-instruct", Name = "Llama 3.3 70B Instruct", ContextLength = 128000, Description = "Open flagship model by Meta" },
            new() { Id = "qwen/qwen-2.5-72b-instruct", Name = "Qwen 2.5 72B Instruct", ContextLength = 128000, Description = "Powerful open multilingual model" },
            new() { Id = "x-ai/grok-2", Name = "Grok 2", ContextLength = 128000, Description = "Frontier intelligence by xAI" }
        ];
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string modelId,
        List<Message> history,
        string? systemPrompt = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = Models.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
                    ?? new GramModel { Id = modelId, Name = modelId, Provider = "OpenRouter" };

        var providerId = model.Provider.ToLowerInvariant();
        var provider = Providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(model.Provider, StringComparison.OrdinalIgnoreCase));
        var apiKey = GetApiKey(provider?.Id ?? "openrouter");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = GetApiKey("openrouter");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                providerId = "openrouter";
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey) && providerId != "ollama")
        {
            yield return $"[API key not configured for {(provider?.Name ?? model.Provider)}. Please open Settings > Intelligence > Providers to configure your API key.]";
            yield break;
        }

        if (providerId == "anthropic" && !modelId.Contains("/"))
        {
            await foreach (var chunk in StreamAnthropicAsync(modelId, history, systemPrompt, apiKey ?? "", cancellationToken))
            {
                yield return chunk;
            }
        }
        else if (providerId == "ollama")
        {
            var baseUrl = provider?.BaseUrl ?? "http://localhost:11434";
            await foreach (var chunk in StreamOllamaAsync(modelId, history, systemPrompt, baseUrl, cancellationToken))
            {
                yield return chunk;
            }
        }
        else
        {
            var endpoint = providerId switch
            {
                "openai" => $"{provider?.BaseUrl ?? "https://api.openai.com/v1"}/chat/completions",
                "xai" => $"{provider?.BaseUrl ?? "https://api.x.ai/v1"}/chat/completions",
                "deepseek" => $"{provider?.BaseUrl ?? "https://api.deepseek.com/v1"}/chat/completions",
                _ => $"{provider?.BaseUrl ?? "https://openrouter.ai/api/v1"}/chat/completions"
            };

            await foreach (var chunk in StreamOpenAICompatibleAsync(endpoint, modelId, history, systemPrompt, apiKey ?? "", providerId == "openrouter", cancellationToken))
            {
                yield return chunk;
            }
        }
    }

    public async Task<string?> GenerateTitleAsync(string modelId, string userPrompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var titleHistory = new List<Message>
            {
                new("user", $"Generate a concise 2 to 5 word title for a conversation that starts with this message. Return ONLY the plain title text without quotes, markdown, or punctuation:\n\n{userPrompt}")
            };
            var system = "You generate short, concise 2 to 5 word titles for conversations. Output only the title text, nothing else.";

            var sb = new System.Text.StringBuilder();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(12));

            await foreach (var chunk in StreamChatAsync(modelId, titleHistory, system, cts.Token))
            {
                if (chunk.StartsWith("[API key") || chunk.StartsWith("[Error")) return null;
                sb.Append(chunk);
            }

            var result = sb.ToString().Trim().Trim('"', '\'', '`', '#', '*', '.', ':');
            if (!string.IsNullOrWhiteSpace(result) && result.Length > 0 && !result.Contains('\n'))
            {
                return result.Length > 48 ? result[..48].Trim() : result;
            }
        }
        catch {}
        return null;
    }

    private async IAsyncEnumerable<string> StreamOpenAICompatibleAsync(
        string endpoint,
        string modelId,
        List<Message> history,
        string? systemPrompt,
        string apiKey,
        bool isOpenRouter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }

        foreach (var msg in history)
        {
            if (!string.IsNullOrWhiteSpace(msg.Text))
            {
                messages.Add(new { role = msg.Role == "assistant" ? "assistant" : "user", content = msg.Text });
            }
        }

        var payload = new
        {
            model = modelId,
            messages,
            stream = true
        };

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (isOpenRouter)
        {
            req.Headers.Add("HTTP-Referer", "https://gram.2helix.com");
            req.Headers.Add("X-Title", "Gram Assistant");
        }

        HttpResponseMessage? resp = null;
        string? networkError = null;
        try
        {
            resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            networkError = $"[Network error: {ex.Message}]";
        }

        if (networkError != null)
        {
            yield return networkError;
            yield break;
        }

        if (resp == null || !resp.IsSuccessStatusCode)
        {
            var errBody = resp != null ? await resp.Content.ReadAsStringAsync(cancellationToken) : "No response";
            yield return $"[API Error {(resp != null ? (int)resp.StatusCode : 0)}: {errBody}]";
            yield break;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) continue;
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data:")) continue;

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]") break;

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var contentProp))
                    {
                        token = contentProp.GetString();
                    }
                    else if (choice.TryGetProperty("text", out var textProp))
                    {
                        token = textProp.GetString();
                    }
                }
            }
            catch {}

            if (!string.IsNullOrEmpty(token))
            {
                yield return token;
            }
        }
    }

    private async IAsyncEnumerable<string> StreamOllamaAsync(
        string modelId,
        List<Message> history,
        string? systemPrompt,
        string baseUrl,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }
        foreach (var msg in history)
        {
            if (!string.IsNullOrWhiteSpace(msg.Text))
            {
                messages.Add(new { role = msg.Role, content = msg.Text });
            }
        }

        var cleanModel = modelId.StartsWith("ollama/", StringComparison.OrdinalIgnoreCase) ? modelId["ollama/".Length..] : modelId;
        var payload = new { model = cleanModel, messages, stream = true };
        var json = JsonSerializer.Serialize(payload);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/chat")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? resp = null;
        string? ollamaError = null;
        try
        {
            resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ollamaError = $"[Ollama connection error: {ex.Message}]";
        }

        if (ollamaError != null)
        {
            yield return ollamaError;
            yield break;
        }

        if (resp == null || !resp.IsSuccessStatusCode)
        {
            var err = resp != null ? await resp.Content.ReadAsStringAsync(cancellationToken) : "No response";
            yield return $"[Ollama error {(resp != null ? (int)resp.StatusCode : 0)}: {err}]";
            yield break;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("message", out var msgObj) && msgObj.TryGetProperty("content", out var contentProp))
                {
                    token = contentProp.GetString();
                }
            }
            catch {}

            if (!string.IsNullOrEmpty(token))
            {
                yield return token;
            }
        }
    }

    private async IAsyncEnumerable<string> StreamAnthropicAsync(
        string modelId,
        List<Message> history,
        string? systemPrompt,
        string apiKey,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = new List<object>();
        foreach (var msg in history)
        {
            if (!string.IsNullOrWhiteSpace(msg.Text))
            {
                messages.Add(new { role = msg.Role == "assistant" ? "assistant" : "user", content = msg.Text });
            }
        }

        var cacheableSystem = string.IsNullOrWhiteSpace(systemPrompt)
            ? Array.Empty<object>()
            : new object[]
            {
                new
                {
                    type = "text",
                    text = systemPrompt,
                    cache_control = new { type = "ephemeral" }
                }
            };

        var payload = new
        {
            model = modelId,
            max_tokens = 4096,
            system = cacheableSystem,
            messages,
            // Anthropic automatically advances this breakpoint as the
            // conversation grows while reusing the stable system prefix.
            cache_control = new { type = "ephemeral" },
            stream = true
        };

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        HttpResponseMessage? resp = null;
        string? anthropicError = null;
        try
        {
            resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            anthropicError = $"[Anthropic connection error: {ex.Message}]";
        }

        if (anthropicError != null)
        {
            yield return anthropicError;
            yield break;
        }

        if (resp == null || !resp.IsSuccessStatusCode)
        {
            var err = resp != null ? await resp.Content.ReadAsStringAsync(cancellationToken) : "No response";
            yield return $"[Anthropic error {(resp != null ? (int)resp.StatusCode : 0)}: {err}]";
            yield break;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) continue;
            line = line.Trim();
            if (!line.StartsWith("data:")) continue;

            var data = line["data:".Length..].Trim();
            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "content_block_delta" &&
                    doc.RootElement.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("text", out var textProp))
                {
                    token = textProp.GetString();
                }
            }
            catch {}

            if (!string.IsNullOrEmpty(token))
            {
                yield return token;
            }
        }
    }
}

public sealed class ProviderHealthStatus
{
    public bool IsHealthy { get; set; }
    public long LatencyMs { get; set; }
    public string Message { get; set; } = "Not checked";
    public DateTimeOffset? LastChecked { get; set; }
}
