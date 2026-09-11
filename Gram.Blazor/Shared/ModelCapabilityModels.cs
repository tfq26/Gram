namespace Gram.Blazor.Shared;

public enum ModelCodingSupportTier
{
    VerifiedAutonomous, // Tier 1: Fully autonomous coding, multi-turn tool loops, and surgical diffs
    VerifiedAssisted,   // Tier 2: Capable of simple edits and file inspection; requires confirmation
    ChatOnly,           // Tier 3: Pure conversational chat and text generation; mutation tools are stripped
    Unsupported         // Tier 4: Fails baseline connectivity or reasoning checks
}

public sealed class ProbeResult
{
    public ProbeResult() { }

    public ProbeResult(string probeName, bool passed, double score, string? diagnosticMessage, long latencyMs)
    {
        ProbeName = probeName;
        Passed = passed;
        Score = score;
        DiagnosticMessage = diagnosticMessage;
        LatencyMs = latencyMs;
    }

    public string ProbeName { get; set; } = "";
    public bool Passed { get; set; }
    public double Score { get; set; }
    public string? DiagnosticMessage { get; set; }
    public long LatencyMs { get; set; }
}

public sealed class ModelQualificationResult
{
    public ModelCodingSupportTier OverallTier { get; set; } = ModelCodingSupportTier.VerifiedAutonomous;
    public bool IsChatSupported { get; set; } = true;
    public ModelCodingSupportTier CodingTier { get; set; } = ModelCodingSupportTier.VerifiedAutonomous;
    public List<ProbeResult> ProbeResults { get; set; } = [];
    public DateTimeOffset QualificationTimestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? UnsupportedReason { get; set; }
}
