namespace Gram.Blazor.Shared;

/// <summary>
/// Keeps the full tool output available to the UI and audit trail while sending
/// a bounded representation back into the model's working context.
/// </summary>
public static class ToolResultReducer
{
    public static string ForModel(string toolName, string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "Tool completed with no output.";

        var limit = toolName.ToLowerInvariant() switch
        {
            "search_web" => 5000,
            "fetch_web_content" => 6000,
            "run_command" => 6500,
            "grep_search" or "find_files" => 5000,
            "view_file" => 9000,
            _ => 7000
        };

        if (output.Length <= limit) return output;

        var headLength = (int)(limit * 0.72);
        var tailLength = limit - headLength;
        return output[..headLength]
            + $"\n\n...[tool output reduced from {output.Length:N0} to {limit:N0} characters; full output remains available in the tool record]...\n\n"
            + output[^tailLength..];
    }
}
