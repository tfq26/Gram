namespace Gram.Blazor.Shared;

public static class GramModelIds
{
    // Stable product roles. The provider model IDs remain implementation details.
    public const string GramQ = "gram-q";
    public const string GramQPlus = "gram-q-plus";
    public const string GramQPlusPlus = "gram-q-plus-plus";
    public const string LingTinyFree = "ling-tiny-free";

    public const string VercelLingFlash = "inclusionai/ling-3.0-flash";
    public const string MuseSparkContributor = "meta/muse-spark-1.3-contributor";
    public const string VercelLingTinyFree = "inclusionai/ling-3.0-flash-fin-free";
    public const string GroqGptOss = "openai/gpt-oss-120b";
    public const string GoogleGeminiFlashLite = "gemini-3.1-flash-lite";

    // Older persisted conversations used this role for Gemini. Keep it readable.
    public const string LegacyGramQPlus = "gram-q-plus";

    public static bool IsSystemModel(string? modelId) =>
        modelId is not null &&
        (modelId.Equals(GramQ, StringComparison.OrdinalIgnoreCase)
         || modelId.Equals(GramQPlus, StringComparison.OrdinalIgnoreCase)
         || modelId.Equals(GramQPlusPlus, StringComparison.OrdinalIgnoreCase));
}

public static class ReasoningEffort
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static string Normalize(string? effort) => (effort ?? "").Trim().ToLowerInvariant() switch
    {
        "low" or "fast" or "1" => Low,
        "high" or "deep" or "3" => High,
        _ => Medium
    };

    public static string GetLabel(string? effort) => Normalize(effort) switch
    {
        Low => "Fast",
        High => "Deep",
        _ => "Balanced"
    };

    public static string GetDescription(string? effort) => Normalize(effort) switch
    {
        Low => "Fast everyday intelligence · Low latency responses",
        High => "Deep architectural reasoning · Extended thinking budget",
        _ => "Balanced agentic coding · High tool & structured reliability"
    };

    public static bool SupportsReasoning(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return true; // Default Gram Q
        var m = modelId.Trim().ToLowerInvariant();
        return m.StartsWith("gram") || m.Contains("gram-q") || m.Equals("q") || m.StartsWith("q-") ||
               m.StartsWith("o1") || m.StartsWith("o3") || m.Contains("/o1") || m.Contains("/o3") ||
               m.Contains("claude-3-7") || m.Contains("claude-3.7") || m.Contains("gpt-5");
    }
}
