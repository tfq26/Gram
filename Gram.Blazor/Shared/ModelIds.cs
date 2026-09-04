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
