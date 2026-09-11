using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public sealed class ThemeColors
{
    [JsonPropertyName("bgBody")]
    public string? BgBody { get; set; }

    [JsonPropertyName("bgPanel")]
    public string? BgPanel { get; set; }

    [JsonPropertyName("bgSubtle")]
    public string? BgSubtle { get; set; }

    [JsonPropertyName("border")]
    public string? Border { get; set; }

    [JsonPropertyName("textPrimary")]
    public string? TextPrimary { get; set; }

    [JsonPropertyName("textSecondary")]
    public string? TextSecondary { get; set; }

    [JsonPropertyName("buttonPrimary")]
    public string? ButtonPrimary { get; set; }

    [JsonPropertyName("buttonText")]
    public string? ButtonText { get; set; }

    [JsonPropertyName("accent")]
    public string? Accent { get; set; }

    [JsonPropertyName("accentText")]
    public string? AccentText { get; set; }

    // Fallback 5-color fields
    [JsonPropertyName("color1")]
    public string Color1 { get; set; } = "#050505";

    [JsonPropertyName("color2")]
    public string Color2 { get; set; } = "#141414";

    [JsonPropertyName("color3")]
    public string Color3 { get; set; } = "#222222";

    [JsonPropertyName("color4")]
    public string Color4 { get; set; } = "#8e8e93";

    [JsonPropertyName("color5")]
    public string Color5 { get; set; } = "#ffffff";

    public string[] ToArray() => [
        BgBody ?? Color1,
        BgPanel ?? Color2,
        Border ?? Color3,
        TextSecondary ?? Color4,
        ButtonPrimary ?? Accent ?? Color5
    ];
}

public sealed class RecentThemeItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("colors")]
    public ThemeColors Colors { get; set; } = new();

    [JsonPropertyName("lastUsed")]
    public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserColorsState
{
    [JsonPropertyName("activeTheme")]
    public string ActiveTheme { get; set; } = "Carbon Black";

    [JsonPropertyName("recentThemes")]
    public List<RecentThemeItem> RecentThemes { get; set; } = new();
}

public sealed class ThemeDefinition
{
    public required string Name { get; init; }
    public required ThemeColors Colors { get; init; }
    public string[] Palette => Colors.ToArray();
}

public sealed class ThemeService
{
    public event Action? OnChange;

    private readonly string colorsDir;
    private readonly string colorsJsonPath;
    private readonly string userColorsJsonPath;

    public string ActiveThemeName { get; private set; } = "Carbon Black";
    public ThemeColors ActiveColors { get; private set; } = new();

    public List<RecentThemeItem> RecentThemes { get; private set; } = new();
    public Dictionary<string, ThemeColors> CatalogThemes { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public ThemeService()
    {
        var currentDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "colors"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "colors"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "colors"),
            Path.Combine(currentDir, "colors"),
            Path.Combine(currentDir, "..", "Resources", "colors"),
            Path.Combine(currentDir, "..", "colors"),
            Path.Combine(currentDir, "..", "..", "..", "colors"),
            Path.Combine(currentDir, "..", "..", "..", "..", "colors"),
            Path.Combine(currentDir, "..", "..", "..", "..", "..", "colors")
        };

        var found = candidates.FirstOrDefault(c => Directory.Exists(c) && File.Exists(Path.Combine(c, "colors.json")));
        colorsDir = found != null ? Path.GetFullPath(found) : Path.GetFullPath(candidates[0]);

        colorsJsonPath = Path.Combine(colorsDir, "colors.json");
        userColorsJsonPath = Path.Combine(colorsDir, "user_colors.json");

        LoadCatalog();
        LoadUserColors();
    }

    public void LoadCatalog()
    {
        try
        {
            if (File.Exists(colorsJsonPath))
            {
                var json = File.ReadAllText(colorsJsonPath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, ThemeColors>>(json);
                if (dict != null)
                {
                    CatalogThemes = new Dictionary<string, ThemeColors>(dict, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ThemeService] Error loading catalog: {ex.Message}");
        }

        if (CatalogThemes.Count == 0)
        {
            CatalogThemes["Carbon Black"] = new ThemeColors
            {
                BgBody = "#050505",
                BgPanel = "#0d0d0d",
                BgSubtle = "#141414",
                Border = "#242424",
                TextPrimary = "#f4f4f5",
                TextSecondary = "#8e8e93",
                ButtonPrimary = "#ffffff",
                ButtonText = "#000000",
                Accent = "#ffffff",
                AccentText = "#ffffff"
            };
        }
    }

    public void LoadUserColors()
    {
        try
        {
            if (File.Exists(userColorsJsonPath))
            {
                var json = File.ReadAllText(userColorsJsonPath);
                var state = JsonSerializer.Deserialize<UserColorsState>(json);
                if (state != null)
                {
                    ActiveThemeName = state.ActiveTheme ?? "Carbon Black";
                    RecentThemes = state.RecentThemes ?? new List<RecentThemeItem>();

                    if (CatalogThemes.TryGetValue(ActiveThemeName, out var loadedColors))
                    {
                        ActiveColors = loadedColors;
                    }
                    else if (RecentThemes.FirstOrDefault(x => x.Name.Equals(ActiveThemeName, StringComparison.OrdinalIgnoreCase)) is { } recent)
                    {
                        ActiveColors = recent.Colors;
                    }
                    else if (CatalogThemes.Count > 0)
                    {
                        var first = CatalogThemes.First();
                        ActiveThemeName = first.Key;
                        ActiveColors = first.Value;
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ThemeService] Error loading user colors: {ex.Message}");
        }

        if (CatalogThemes.TryGetValue("Carbon Black", out var def))
        {
            ActiveThemeName = "Carbon Black";
            ActiveColors = def;
        }
    }

    public void ApplyTheme(string name, ThemeColors colors)
    {
        ActiveThemeName = name;
        ActiveColors = colors;

        var existing = RecentThemes.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.LastUsed = DateTimeOffset.UtcNow;
            existing.Colors = colors;
        }
        else
        {
            RecentThemes.Insert(0, new RecentThemeItem
            {
                Name = name,
                Colors = colors,
                LastUsed = DateTimeOffset.UtcNow
            });
        }

        if (RecentThemes.Count > 6)
        {
            RecentThemes = RecentThemes.Take(6).ToList();
        }

        SaveUserColors();
        OnChange?.Invoke();
    }

    public void SelectCatalogTheme(string name)
    {
        if (CatalogThemes.TryGetValue(name, out var colors))
        {
            ApplyTheme(name, colors);
        }
    }

    public void SaveUserColors()
    {
        try
        {
            Directory.CreateDirectory(colorsDir);
            var state = new UserColorsState
            {
                ActiveTheme = ActiveThemeName,
                RecentThemes = RecentThemes
            };
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(userColorsJsonPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ThemeService] Error saving user colors: {ex.Message}");
        }
    }

    public string GetInlineCssVariables()
    {
        var bgBody = !string.IsNullOrEmpty(ActiveColors.BgBody) ? ActiveColors.BgBody : ActiveColors.Color1;
        var bgPanel = !string.IsNullOrEmpty(ActiveColors.BgPanel) ? ActiveColors.BgPanel : ActiveColors.Color2;
        var bgSubtle = !string.IsNullOrEmpty(ActiveColors.BgSubtle) ? ActiveColors.BgSubtle : ActiveColors.Color2;
        var border = !string.IsNullOrEmpty(ActiveColors.Border) ? ActiveColors.Border : ActiveColors.Color3;
        var textPrimary = !string.IsNullOrEmpty(ActiveColors.TextPrimary) ? ActiveColors.TextPrimary : "#f4f4f5";
        var textSecondary = !string.IsNullOrEmpty(ActiveColors.TextSecondary) ? ActiveColors.TextSecondary : ActiveColors.Color4;
        var buttonPrimary = !string.IsNullOrEmpty(ActiveColors.ButtonPrimary) ? ActiveColors.ButtonPrimary : (ActiveColors.Accent ?? ActiveColors.Color5);
        var buttonText = !string.IsNullOrEmpty(ActiveColors.ButtonText) 
            ? ActiveColors.ButtonText 
            : (buttonPrimary.Equals("#ffffff", StringComparison.OrdinalIgnoreCase) || buttonPrimary.Equals("#f4f4f5", StringComparison.OrdinalIgnoreCase) ? "#000000" : "#ffffff");
        var accent = !string.IsNullOrEmpty(ActiveColors.Accent) ? ActiveColors.Accent : ActiveColors.Color5;
        var accentText = !string.IsNullOrEmpty(ActiveColors.AccentText) ? ActiveColors.AccentText : ActiveColors.Color5;

        // Focus border calculation:
        // Avoid harsh 100% white or glaring high-contrast rings that look cheap.
        // For white/monochrome accents, use a refined, subtle 22% white border.
        // For colored accents, blend subtly with border (45% accent, 55% border) for disciplined contrast.
        bool isLightAccent = accent.Equals("#ffffff", StringComparison.OrdinalIgnoreCase) ||
                             accent.Equals("#fff", StringComparison.OrdinalIgnoreCase) ||
                             accent.Equals("#f4f4f5", StringComparison.OrdinalIgnoreCase) ||
                             accent.Equals("#f5f5f5", StringComparison.OrdinalIgnoreCase) ||
                             accent.Equals("#f4f7f5", StringComparison.OrdinalIgnoreCase) ||
                             accent.Equals("#eee5e9", StringComparison.OrdinalIgnoreCase);

        var borderFocus = isLightAccent
            ? "rgba(255, 255, 255, 0.22)"
            : $"color-mix(in srgb, {accent} 45%, {border})";

        return $"--bg-body: {bgBody}; " +
               $"--bg-panel: {bgPanel}; " +
               $"--bg-panel-subtle: {bgSubtle}; " +
               $"--border-color: {border}; " +
               $"--border-focus: {borderFocus}; " +
               $"--focus-ring: transparent; " +
               $"--text-primary: {textPrimary}; " +
               $"--text-secondary: {textSecondary}; " +
               $"--text-muted: {textSecondary}; " +
               $"--btn-primary-bg: {buttonPrimary}; " +
               $"--btn-primary-text: {buttonText}; " +
               $"--accent: {accent}; " +
               $"--accent-hover: {accent}; " +
               $"--accent-text: {accentText}; " +
               $"--tabs-scroll-thumb: {border}; " +
               $"--tabs-scroll-thumb-hover: {accent}; " +
               $"--accent-light: {border}; " +
               $"--accent-grad: linear-gradient(135deg, {accent} 0%, {bgSubtle} 100%);";
    }
}
