namespace Gram.Blazor.Shared;

public static class FeatureFlags
{
    // Presentation generation is intentionally out of the pre-1.0 product surface.
    // It can be re-enabled for development with GRAM_ENABLE_PRESENTATIONS=true.
    public static bool PresentationsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("GRAM_ENABLE_PRESENTATIONS"), "true", StringComparison.OrdinalIgnoreCase);

    // Reserved for future desktop releases. The connector remains discoverable
    // while execution is disabled by default.
    public static bool DesktopUseEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("GRAM_ENABLE_DESKTOP_USE"), "true", StringComparison.OrdinalIgnoreCase);
}
