using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ContextMeterServiceUnitTests
{
    [Theory]
    [InlineData("google/gemini-2.5-pro", 2_000_000)]
    [InlineData("google/gemini-2.5-flash", 1_000_000)]
    [InlineData("anthropic/claude-3.7-sonnet", 200_000)]
    [InlineData("anthropic/claude-3.5-sonnet", 200_000)]
    [InlineData("openai/gpt-4o", 128_000)]
    [InlineData("deepseek/deepseek-r1", 64_000)]
    [InlineData("unknown-model", 128_000)]
    public void ResolveContextLimit_ResolvesKnownModelContexts(string modelId, int expectedLimit)
    {
        var limit = ContextMeterService.ResolveContextLimit(modelId);
        Assert.Equal(expectedLimit, limit);
    }

    [Fact]
    public void ResolveContextLimit_ConfiguredLimitTakesPrecedence()
    {
        var limit = ContextMeterService.ResolveContextLimit("openai/gpt-4o", 500_000);
        Assert.Equal(500_000, limit);
    }

    [Fact]
    public void CalculateBreakdown_CalculatesSystemAndRulesTokens()
    {
        var breakdown = ContextMeterService.CalculateBreakdown(
            messages: null,
            draft: null,
            customRules: "Always reply in C# and .NET 10 style.",
            modelId: "openai/gpt-4o");

        Assert.True(breakdown.SystemTokens > 1500);
        Assert.Equal(0, breakdown.MessagesTokens);
        Assert.Equal(0, breakdown.ToolTokens);
        Assert.Equal(0, breakdown.DraftTokens);
        Assert.Equal(breakdown.SystemTokens, breakdown.TotalTokens);
        Assert.Equal(128_000, breakdown.ContextLimit);
        Assert.True(breakdown.RemainingTokens > 100_000);
        Assert.Equal(ContextHealthStatus.Healthy, breakdown.HealthStatus);
    }

    [Fact]
    public void CalculateBreakdown_IncludesMessagesAndToolCalls()
    {
        var messages = new List<Message>
        {
            new("user", "Can you check the git status and list files?"),
            new("assistant", "Sure, let me check that for you.")
            {
                ToolCalls = new List<ToolCallInfo>
                {
                    new()
                    {
                        Id = "tc-1",
                        ToolName = "run_command",
                        Arguments = "{\"CommandLine\":\"git status\"}",
                        Result = "On branch main\nChanges not staged for commit:\n  modified: app.cs\n"
                    }
                }
            }
        };

        var breakdown = ContextMeterService.CalculateBreakdown(
            messages: messages,
            draft: "Next command",
            customRules: null,
            modelId: "anthropic/claude-3.7-sonnet");

        Assert.Equal(2, breakdown.MessageCount);
        Assert.Equal(1, breakdown.ToolCallCount);
        Assert.True(breakdown.MessagesTokens > 0);
        Assert.True(breakdown.ToolTokens > 0);
        Assert.True(breakdown.DraftTokens > 0);
        Assert.Equal(200_000, breakdown.ContextLimit);
        Assert.True(breakdown.TotalTokens > breakdown.SystemTokens);
    }

    [Theory]
    [InlineData(100, "100")]
    [InlineData(1_500, "1.5k")]
    [InlineData(14_250, "14.3k")]
    [InlineData(128_000, "128k")]
    [InlineData(1_000_000, "1M")]
    [InlineData(2_000_000, "2M")]
    public void FormatTokens_FormatsCorrectly(int tokens, string expected)
    {
        var formatted = ContextMeterService.FormatTokens(tokens);
        Assert.Equal(expected, formatted);
    }
}
