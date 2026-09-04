namespace Gram.Blazor.Shared;

/// Extracts only high-signal, user-authored memory candidates. It intentionally
/// does not use an LLM: automatic extraction must be deterministic and reviewable.
public sealed class MemoryExtractionService
{
    private readonly IStructuredMemoryService _memory;

    public MemoryExtractionService(IStructuredMemoryService memory) => _memory = memory;

    public async Task<IReadOnlyList<StructuredMemory>> ExtractAsync(string text, string? conversationId = null, string? projectId = null, string userId = "default", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var input = text.Trim();
        var explicitRequest = input.StartsWith("remember that ", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("please remember ", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("don't forget that ", StringComparison.OrdinalIgnoreCase);
        var candidates = new List<RememberMemoryRequest>();

        var remembered = explicitRequest ? input[(input.IndexOf(' ') + 1)..].Trim() : input;
        if (remembered.StartsWith("remember ", StringComparison.OrdinalIgnoreCase)) remembered = remembered[9..].Trim();
        if (remembered.StartsWith("that ", StringComparison.OrdinalIgnoreCase)) remembered = remembered[5..].Trim();

        var preference = System.Text.RegularExpressions.Regex.Match(remembered, @"^(?:I|we)\s+(?:really\s+)?prefer\s+(?<value>.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (preference.Success)
            candidates.Add(new("User", "prefers", TrimSentence(preference.Groups["value"].Value), StructuredMemoryKinds.Preference));

        var relationship = System.Text.RegularExpressions.Regex.Match(remembered, @"^my\s+(?<relation>girlfriend|boyfriend|partner|wife|husband)\s+(?:is\s+named|is\s+called|named)\s+(?<value>.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (relationship.Success)
            candidates.Add(new(TrimSentence(relationship.Groups["relation"].Value), "is named", TrimSentence(relationship.Groups["value"].Value), StructuredMemoryKinds.Relationship));

        var fact = System.Text.RegularExpressions.Regex.Match(remembered, @"^(?:I\s+am|my\s+name\s+is|I\s+work\s+as)\s+(?<value>.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (fact.Success)
            candidates.Add(new("User", "is", TrimSentence(fact.Groups["value"].Value), StructuredMemoryKinds.Fact));

        // Unknown statements are never stored automatically, even if prefixed by
        // conversational language that merely sounds like a memory request.
        var results = new List<StructuredMemory>();
        foreach (var candidate in candidates)
        {
            results.Add(await _memory.RememberAsync(candidate with
            {
                UserId = userId,
                ProjectId = projectId,
                SourceConversationId = conversationId,
                Evidence = text.Trim(),
                RequireConfirmation = !explicitRequest,
                Confidence = explicitRequest ? 0.95 : 0.65,
                Importance = candidate.Kind == StructuredMemoryKinds.Relationship ? 0.75 : 0.55
            }, cancellationToken));
        }
        return results;
    }

    private static string TrimSentence(string value) => value.Trim().TrimEnd('.', '!', '?').Trim();
}
