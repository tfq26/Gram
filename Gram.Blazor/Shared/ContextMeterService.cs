namespace Gram.Blazor.Shared;

public enum ContextHealthStatus
{
    Healthy,   // < 50%
    Warning,   // 50% - 80%
    Critical   // > 80%
}

public sealed record ContextUsageBreakdown(
    int SystemTokens,
    int MessagesTokens,
    int ToolTokens,
    int DraftTokens,
    int TotalTokens,
    int ContextLimit,
    int MessageCount,
    int ToolCallCount)
{
    public int RemainingTokens => Math.Max(0, ContextLimit - TotalTokens);
    public double UsagePercentage => ContextLimit <= 0 ? 0 : Math.Clamp((double)TotalTokens / ContextLimit * 100.0, 0.0, 100.0);
    public ContextHealthStatus HealthStatus =>
        UsagePercentage >= 80.0 ? ContextHealthStatus.Critical :
        UsagePercentage >= 50.0 ? ContextHealthStatus.Warning :
        ContextHealthStatus.Healthy;

    public double SystemPercentage => ContextLimit <= 0 ? 0 : Math.Clamp((double)SystemTokens / ContextLimit * 100.0, 0.0, 100.0);
    public double MessagesPercentage => ContextLimit <= 0 ? 0 : Math.Clamp((double)MessagesTokens / ContextLimit * 100.0, 0.0, 100.0);
    public double ToolPercentage => ContextLimit <= 0 ? 0 : Math.Clamp((double)ToolTokens / ContextLimit * 100.0, 0.0, 100.0);
    public double DraftPercentage => ContextLimit <= 0 ? 0 : Math.Clamp((double)DraftTokens / ContextLimit * 100.0, 0.0, 100.0);
}

public static class ContextMeterService
{
    public const int DefaultContextLimit = 128_000;
    private const double CharsPerToken = 3.8;
    private const int BaselineSystemTokens = 1_500;
    private const int MessageEnvelopeTokens = 4;

    public static int ResolveContextLimit(string? modelId, int? configuredLimit = null)
    {
        if (configuredLimit.HasValue && configuredLimit.Value > 0)
        {
            return configuredLimit.Value;
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            return DefaultContextLimit;
        }

        var lower = modelId.ToLowerInvariant();
        if (lower.Contains("gemini-2.5-pro") || lower.Contains("gemini-1.5-pro"))
        {
            return 2_000_000;
        }
        if (lower.Contains("gemini-2.5-flash") || lower.Contains("gemini-1.5-flash") || lower.Contains("gemini-2.0"))
        {
            return 1_000_000;
        }
        if (lower.Contains("claude-3-7") || lower.Contains("claude-3.7") || lower.Contains("claude-3-5") || lower.Contains("claude-3.5"))
        {
            return 200_000;
        }
        if (lower.Contains("o3-mini") || lower.Contains("o1-mini") || lower.Contains("o1-preview") || lower.Contains("o1"))
        {
            return 200_000;
        }
        if (lower.Contains("gpt-4o") || lower.Contains("gpt-4-turbo") || lower.Contains("llama-3.3") || lower.Contains("qwen-2.5"))
        {
            return 128_000;
        }
        if (lower.Contains("deepseek-r1") || lower.Contains("deepseek-chat") || lower.Contains("deepseek-v3"))
        {
            return 64_000;
        }

        return DefaultContextLimit;
    }

    public static ContextUsageBreakdown CalculateBreakdown(
        IReadOnlyList<Message>? messages,
        string? draft = null,
        string? customRules = null,
        string? modelId = null,
        int? configuredLimit = null,
        IReadOnlyList<ToolCallInfo>? activeStreamingTools = null)
    {
        var limit = ResolveContextLimit(modelId, configuredLimit);

        // 1. System Prompt & Rules
        int systemTokens = BaselineSystemTokens;
        if (!string.IsNullOrWhiteSpace(customRules))
        {
            systemTokens += (int)Math.Ceiling(customRules.Length / CharsPerToken);
        }

        // 2. Messages & Tool Execution Results
        int messagesTokens = 0;
        int toolTokens = 0;
        int messageCount = 0;
        int toolCount = 0;

        if (messages != null)
        {
            foreach (var msg in messages)
            {
                messageCount++;
                int msgChars = (msg.Text ?? "").Length;
                if (!string.IsNullOrEmpty(msg.Thinking))
                {
                    msgChars += msg.Thinking.Length;
                }
                messagesTokens += MessageEnvelopeTokens + (int)Math.Ceiling(msgChars / CharsPerToken);

                if (msg.ToolCalls != null)
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        toolCount++;
                        int tcChars = (tc.ToolName ?? "").Length + (tc.Arguments ?? "").Length + (tc.Result ?? "").Length;
                        toolTokens += (int)Math.Ceiling(tcChars / CharsPerToken);
                    }
                }
            }
        }

        // 3. Active streaming tool calls if present
        if (activeStreamingTools != null)
        {
            foreach (var tc in activeStreamingTools)
            {
                toolCount++;
                int tcChars = (tc.ToolName ?? "").Length + (tc.Arguments ?? "").Length + (tc.Result ?? "").Length;
                toolTokens += (int)Math.Ceiling(tcChars / CharsPerToken);
            }
        }

        // 4. Draft text
        int draftTokens = 0;
        if (!string.IsNullOrWhiteSpace(draft))
        {
            draftTokens = (int)Math.Ceiling(draft.Length / CharsPerToken);
        }

        int total = systemTokens + messagesTokens + toolTokens + draftTokens;

        return new ContextUsageBreakdown(
            SystemTokens: systemTokens,
            MessagesTokens: messagesTokens,
            ToolTokens: toolTokens,
            DraftTokens: draftTokens,
            TotalTokens: total,
            ContextLimit: limit,
            MessageCount: messageCount,
            ToolCallCount: toolCount);
    }

    public static string FormatTokens(int tokens)
    {
        if (tokens >= 1_000_000)
        {
            return $"{(double)tokens / 1_000_000:0.#}M";
        }
        if (tokens >= 10_000)
        {
            return $"{(double)tokens / 1_000:0.#}k";
        }
        if (tokens >= 1_000)
        {
            return $"{(double)tokens / 1_000:0.#}k";
        }
        return tokens.ToString("N0");
    }

    public static string FormatPercentage(double percentage)
    {
        return $"{percentage:0.#}%";
    }
}
