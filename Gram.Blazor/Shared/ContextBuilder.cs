using System.Text;

namespace Gram.Blazor.Shared;

public sealed class ContextAssemblyResult
{
    public string SystemPrompt { get; set; } = "";
    public List<Message> Messages { get; set; } = [];
    public int EstimatedTokens { get; set; }
}

public sealed class ContextBuilder
{
    private readonly SessionStorageService _sessionStorage;
    private readonly SettingsService _settingsService;

    public ContextBuilder(SessionStorageService sessionStorage, SettingsService settingsService)
    {
        _sessionStorage = sessionStorage;
        _settingsService = settingsService;
    }

    public async Task<ContextAssemblyResult> AssembleContextAsync(
        Conversation conversation,
        string? dynamicSystemPrompt = null,
        int maxRecentMessages = 16)
    {
        var sbSystem = new StringBuilder();

        // 1. Base System Instructions
        var baseInstructions = !string.IsNullOrWhiteSpace(dynamicSystemPrompt)
            ? dynamicSystemPrompt
            : !string.IsNullOrWhiteSpace(conversation.SystemPromptOverride)
                ? conversation.SystemPromptOverride
                : _settingsService.GetSetting("customInstructions", "You are Gram, a fast, thoughtful, and capable AI assistant.");

        sbSystem.AppendLine(baseInstructions.Trim());

        // 2. Chat-specific Rules
        if (!string.IsNullOrWhiteSpace(conversation.Rules))
        {
            sbSystem.AppendLine();
            sbSystem.AppendLine("<rules>");
            sbSystem.AppendLine(conversation.Rules.Trim());
            sbSystem.AppendLine("</rules>");
        }

        // 3. Compacted Context Summary (Rolling memory from earlier parts of the conversation)
        var summary = await _sessionStorage.GetContextSummaryAsync(conversation.Id);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            sbSystem.AppendLine();
            sbSystem.AppendLine("<context_summary>");
            sbSystem.AppendLine(summary.Trim());
            sbSystem.AppendLine("</context_summary>");
        }

        // 4. Attached Files Context Injection
        if (conversation.Attachments.Count > 0)
        {
            sbSystem.AppendLine();
            sbSystem.AppendLine("<workspace_attachments>");
            foreach (var att in conversation.Attachments)
            {
                if (att.IsText)
                {
                    var text = await _sessionStorage.ReadAttachmentTextAsync(conversation.Id, att.FileName);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Cap individual file inject to 30,000 characters for token protection
                        var safeText = text.Length > 30000 ? text[..30000] + "\n...[truncated]" : text;
                        sbSystem.AppendLine($"<file name=\"{att.FileName}\" size=\"{att.SizeBytes}B\">");
                        sbSystem.AppendLine(safeText);
                        sbSystem.AppendLine("</file>");
                    }
                }
                else
                {
                    sbSystem.AppendLine($"<file name=\"{att.FileName}\" type=\"{att.ContentType}\" size=\"{att.SizeBytes}B\" />");
                }
            }
            sbSystem.AppendLine("</workspace_attachments>");
        }

        // 5. Active Generated Artifacts Reference
        if (conversation.Artifacts.Count > 0)
        {
            sbSystem.AppendLine();
            sbSystem.AppendLine("<workspace_artifacts>");
            foreach (var art in conversation.Artifacts)
            {
                sbSystem.AppendLine($"- {art.FileName} ({art.Language}): {art.Title}");
            }
            sbSystem.AppendLine("</workspace_artifacts>");
        }

        // 6. Assemble Recent Message Window
        var historyMessages = conversation.Messages;
        var recentMessages = historyMessages.Count > maxRecentMessages
            ? historyMessages.TakeLast(maxRecentMessages).ToList()
            : historyMessages.ToList();

        var systemPromptFinal = sbSystem.ToString().Trim();
        var estimatedTokens = (systemPromptFinal.Length + recentMessages.Sum(m => m.Text.Length)) / 4;

        return new ContextAssemblyResult
        {
            SystemPrompt = systemPromptFinal,
            Messages = recentMessages,
            EstimatedTokens = estimatedTokens
        };
    }

    public bool ShouldGenerateSummary(Conversation conversation)
    {
        return conversation.Messages.Count >= 14 && string.IsNullOrWhiteSpace(conversation.ContextSummary);
    }

    public string BuildSummarizationPrompt(Conversation conversation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Summarize the preceding conversation into a structured memory checkpoint for continuous context retention. Include:");
        sb.AppendLine("1. Primary user intent and objectives");
        sb.AppendLine("2. Key decisions made and technical solutions established");
        sb.AppendLine("3. Current active state or pending items");
        sb.AppendLine();
        sb.AppendLine("Keep it concise, bulleted, and actionable.");
        return sb.ToString();
    }
}
