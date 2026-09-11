using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ModelQualificationTests
{
    [Fact]
    public void EvaluateProbes_AllProbesPass_AssignsVerifiedAutonomous()
    {
        var probes = new List<ProbeResult>
        {
            new("ChatReasoning", true, 1.0, "Chat reasoning passed", 120),
            new("ToolCallingGrammar", true, 1.0, "Tool calling valid", 150),
            new("SurgicalPatchAccuracy", true, 0.95, "Surgical patch exact", 200),
            new("ContextCapacity", true, 1.0, "Context constraints honored", 180)
        };

        var result = ModelCapabilityProbeService.EvaluateProbes(probes);

        Assert.Equal(ModelCodingSupportTier.VerifiedAutonomous, result.OverallTier);
        Assert.True(result.IsChatSupported);
        Assert.Equal(ModelCodingSupportTier.VerifiedAutonomous, result.CodingTier);
        Assert.Null(result.UnsupportedReason);
        Assert.Equal(4, result.ProbeResults.Count);
    }

    [Fact]
    public void EvaluateProbes_WeakPatchAccuracy_AssignsVerifiedAssisted()
    {
        var probes = new List<ProbeResult>
        {
            new("ChatReasoning", true, 1.0, "Chat reasoning passed", 120),
            new("ToolCallingGrammar", true, 1.0, "Tool calling valid", 150),
            new("SurgicalPatchAccuracy", true, 0.65, "Moderate patch accuracy", 200),
            new("ContextCapacity", true, 1.0, "Context constraints honored", 180)
        };

        var result = ModelCapabilityProbeService.EvaluateProbes(probes);

        Assert.Equal(ModelCodingSupportTier.VerifiedAssisted, result.OverallTier);
        Assert.True(result.IsChatSupported);
        Assert.Equal(ModelCodingSupportTier.VerifiedAssisted, result.CodingTier);
        Assert.Contains("assisted coding", result.UnsupportedReason);
    }

    [Fact]
    public void EvaluateProbes_FailedToolCalling_AssignsChatOnly()
    {
        var probes = new List<ProbeResult>
        {
            new("ChatReasoning", true, 1.0, "Chat reasoning passed", 120),
            new("ToolCallingGrammar", false, 0.0, "Failed to output valid tool call schema", 300),
            new("SurgicalPatchAccuracy", false, 0.2, "Mismatched indentations", 400),
            new("ContextCapacity", true, 1.0, "Context constraints honored", 250)
        };

        var result = ModelCapabilityProbeService.EvaluateProbes(probes);

        Assert.Equal(ModelCodingSupportTier.ChatOnly, result.OverallTier);
        Assert.True(result.IsChatSupported);
        Assert.Equal(ModelCodingSupportTier.ChatOnly, result.CodingTier);
        Assert.NotNull(result.UnsupportedReason);
        Assert.Contains("tool-calling grammar", result.UnsupportedReason);
    }

    [Fact]
    public void EvaluateProbes_FailedChatReasoning_AssignsUnsupported()
    {
        var probes = new List<ProbeResult>
        {
            new("ChatReasoning", false, 0.0, "Model did not respond or failed connectivity", 15000),
            new("ToolCallingGrammar", false, 0.0, "Skipped", 0),
            new("SurgicalPatchAccuracy", false, 0.0, "Skipped", 0),
            new("ContextCapacity", false, 0.0, "Skipped", 0)
        };

        var result = ModelCapabilityProbeService.EvaluateProbes(probes);

        Assert.Equal(ModelCodingSupportTier.Unsupported, result.OverallTier);
        Assert.False(result.IsChatSupported);
        Assert.Equal(ModelCodingSupportTier.Unsupported, result.CodingTier);
        Assert.NotNull(result.UnsupportedReason);
        Assert.Contains("conversational and reasoning", result.UnsupportedReason);
    }

    [Fact]
    public void EvaluateProbes_FailedContextCapacity_AssignsVerifiedAssisted()
    {
        var probes = new List<ProbeResult>
        {
            new("ChatReasoning", true, 1.0, "Chat reasoning passed", 120),
            new("ToolCallingGrammar", true, 1.0, "Tool calling valid", 150),
            new("SurgicalPatchAccuracy", true, 0.9, "Surgical patch exact", 200),
            new("ContextCapacity", false, 0.3, "Context constraint drift", 450)
        };

        var result = ModelCapabilityProbeService.EvaluateProbes(probes);

        Assert.Equal(ModelCodingSupportTier.VerifiedAssisted, result.OverallTier);
        Assert.True(result.IsChatSupported);
        Assert.Equal(ModelCodingSupportTier.VerifiedAssisted, result.CodingTier);
        Assert.Contains("assisted coding", result.UnsupportedReason);
    }
}
