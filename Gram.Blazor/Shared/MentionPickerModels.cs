namespace Gram.Blazor.Shared;

public sealed record MentionPickerItem(string Id, MentionKind Kind, string Title, string Description, string Category, string Target);
