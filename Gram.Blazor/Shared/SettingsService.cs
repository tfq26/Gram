using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Gram.Blazor.Shared;

public sealed class EnvironmentDefinition
{
    [JsonPropertyName("supportedFeatures")]
    public Dictionary<string, bool> SupportedFeatures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("settings")]
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AppSettingsSchema
{
    [JsonPropertyName("environments")]
    public Dictionary<string, EnvironmentDefinition> Environments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SettingsService
{
    public event Action? OnChange;
    public event Action? OnBreadcrumbChanged;

    public List<string> BreadcrumbParts { get; private set; } = ["Settings", "General"];

    public void SetBreadcrumb(params string[] parts)
    {
        BreadcrumbParts = parts.ToList();
        OnBreadcrumbChanged?.Invoke();
    }

    private readonly string storageDir;
    private readonly string settingsJsonPath;
    public string CurrentEnvironment { get; }

    public AppSettingsSchema Schema { get; private set; } = new();

    public EnvironmentDefinition Current => Schema.Environments.TryGetValue(CurrentEnvironment, out var env)
        ? env
        : Schema.Environments.GetValueOrDefault("Web") ?? new EnvironmentDefinition();

    public SettingsService(IConfiguration configuration, StorageService storageService)
    {
        // Detect environment from config / build parameter / environment variable
        var envFromConfig = configuration["Gram:Environment"]
                            ?? Environment.GetEnvironmentVariable("GRAM_ENVIRONMENT");

        if (!string.IsNullOrWhiteSpace(envFromConfig))
        {
            CurrentEnvironment = envFromConfig;
        }
        else if (OperatingSystem.IsWindows())
        {
            CurrentEnvironment = "Desktop-Windows";
        }
        else if (OperatingSystem.IsMacOS())
        {
            CurrentEnvironment = "Desktop-Mac";
        }
        else
        {
            CurrentEnvironment = "Web";
        }

        storageDir = storageService.DataDirectory;
        settingsJsonPath = storageService.GetFilePath("settings.json");

        storageService.EnsureDirectories();
        LoadSettings();
    }

    public void LoadSettings()
    {
        try
        {
            if (File.Exists(settingsJsonPath))
            {
                var json = File.ReadAllText(settingsJsonPath);
                var loaded = JsonSerializer.Deserialize<AppSettingsSchema>(json);
                if (loaded != null && loaded.Environments.Count > 0)
                {
                    Schema = loaded;
                    EnsureDefaultEnvironments();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsService] Error reading settings.json: {ex.Message}");
        }

        InitializeDefaultSchema();
        SaveSettings();
    }

    private void EnsureDefaultEnvironments()
    {
        if (!Schema.Environments.ContainsKey("Web"))
        {
            Schema.Environments["Web"] = CreateWebEnvironment();
        }
        if (!Schema.Environments.ContainsKey("Desktop-Windows"))
        {
            Schema.Environments["Desktop-Windows"] = CreateWindowsEnvironment();
        }
        if (!Schema.Environments.ContainsKey("Desktop-Mac"))
        {
            Schema.Environments["Desktop-Mac"] = CreateMacEnvironment();
        }
    }

    private void InitializeDefaultSchema()
    {
        Schema = new AppSettingsSchema
        {
            Environments = new Dictionary<string, EnvironmentDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["Web"] = CreateWebEnvironment(),
                ["Desktop-Windows"] = CreateWindowsEnvironment(),
                ["Desktop-Mac"] = CreateMacEnvironment()
            }
        };
    }

    private static EnvironmentDefinition CreateWebEnvironment() => new()
    {
        SupportedFeatures = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["assistantIdentity"] = true,
            ["personaTone"] = true,
            ["audioInput"] = true,
            ["cloudProviders"] = true,
            ["wakeWord"] = false,
            ["globalHotkey"] = false,
            ["systemStartup"] = false,
            ["localFileStorage"] = false,
            ["localMcpServers"] = false,
            ["localOllama"] = false,
            ["authAndStorage"] = false,
            ["autoSendVoice"] = true,
            ["audioChime"] = true
        },
        Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["assistantName"] = "Gram",
            ["personaTone"] = "concise",
            ["autoSendVoice"] = "true",
            ["playVoiceChime"] = "true"
        }
    };

    private static EnvironmentDefinition CreateWindowsEnvironment() => new()
    {
        SupportedFeatures = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["assistantIdentity"] = true,
            ["personaTone"] = true,
            ["audioInput"] = true,
            ["cloudProviders"] = true,
            ["wakeWord"] = true,
            ["globalHotkey"] = true,
            ["systemStartup"] = true,
            ["localFileStorage"] = true,
            ["localMcpServers"] = true,
            ["localOllama"] = true,
            ["authAndStorage"] = true,
            ["autoSendVoice"] = true,
            ["audioChime"] = true
        },
        Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["assistantName"] = "Gram",
            ["personaTone"] = "programmer",
            ["wakeWord"] = "Hey Gram",
            ["globalHotkey"] = "Alt + Space",
            ["autoSendVoice"] = "true",
            ["playVoiceChime"] = "true",
            ["startAtLogin"] = "false"
        }
    };

    private static EnvironmentDefinition CreateMacEnvironment() => new()
    {
        SupportedFeatures = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["assistantIdentity"] = true,
            ["personaTone"] = true,
            ["audioInput"] = true,
            ["cloudProviders"] = true,
            ["wakeWord"] = true,
            ["globalHotkey"] = true,
            ["systemStartup"] = true,
            ["localFileStorage"] = true,
            ["localMcpServers"] = true,
            ["localOllama"] = true,
            ["authAndStorage"] = true,
            ["autoSendVoice"] = true,
            ["audioChime"] = true
        },
        Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["assistantName"] = "Gram",
            ["personaTone"] = "programmer",
            ["wakeWord"] = "Hey Gram",
            ["globalHotkey"] = "Cmd + Space",
            ["autoSendVoice"] = "true",
            ["playVoiceChime"] = "true",
            ["startAtLogin"] = "true"
        }
    };

    public bool IsSelfHosted => !string.Equals(CurrentEnvironment, "Web", StringComparison.OrdinalIgnoreCase);

    public bool IsFeatureSupported(string featureKey)
    {
        return Current.SupportedFeatures.TryGetValue(featureKey, out var supported) && supported;
    }

    public string GetSetting(string key, string defaultValue = "")
    {
        return Current.Settings.TryGetValue(key, out var val) ? val : defaultValue;
    }

    public void SetSetting(string key, string value)
    {
        Current.Settings[key] = value;
        SaveSettings();
    }

    public void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(Schema, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsJsonPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsService] Error saving settings.json: {ex.Message}");
        }
        OnChange?.Invoke();
    }
}
