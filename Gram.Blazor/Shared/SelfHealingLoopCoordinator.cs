using System.Diagnostics;

namespace Gram.Blazor.Shared;

public sealed class SelfHealingOptions
{
    public int MaxAttempts { get; set; } = 3;
    public int MaxCumulativeTimeSeconds { get; set; } = 90;
    public bool AutoRollbackOnRepeatedBlocker { get; set; } = true;
}

public sealed class SelfHealingState
{
    public int AttemptCount { get; internal set; }
    public List<string> SeenErrorHashes { get; } = [];
    public bool HasRepetitionLoop { get; internal set; }
    public bool ExceededAttemptBudget { get; internal set; }
    public bool ExceededTimeBudget { get; internal set; }
    public string? LastErrorHash { get; internal set; }
    public IReadOnlyList<CompilerDiagnostic> LastDiagnostics { get; internal set; } = [];
    public long CumulativeDurationMs { get; internal set; }
}

public sealed class SelfHealingLoopCoordinator
{
    private readonly SelfHealingOptions _options;
    private readonly Stopwatch _stopwatch = new();
    private readonly SelfHealingState _state = new();
    private string? _preRepairCheckpointId;

    public SelfHealingState State => _state;
    public SelfHealingOptions Options => _options;

    public SelfHealingLoopCoordinator(SelfHealingOptions? options = null)
    {
        _options = options ?? new SelfHealingOptions();
        _stopwatch.Start();
    }

    public void SetPreRepairCheckpoint(string checkpointId)
    {
        _preRepairCheckpointId = checkpointId;
    }

    public bool RecordValidationRun(string compilerOutput, int exitCode, out string? haltReason)
    {
        _state.AttemptCount++;
        _state.CumulativeDurationMs = _stopwatch.ElapsedMilliseconds;
        haltReason = null;

        var diagnostics = CompilerDiagnosticParser.Parse(compilerOutput);
        _state.LastDiagnostics = diagnostics;

        if (exitCode == 0 && diagnostics.Count(d => d.Severity == "ERROR") == 0)
        {
            // Clean pass!
            return true;
        }

        var errorHash = CompilerDiagnosticParser.ComputeErrorHash(diagnostics);
        _state.LastErrorHash = errorHash;

        // Check Repetition: If the exact same error hash occurred previously
        if (_state.SeenErrorHashes.Contains(errorHash))
        {
            _state.HasRepetitionLoop = true;
            haltReason = $"Self-healing halted: Same compiler error repeated across iterations (Error Hash: {errorHash}). Breaking out of loop to prevent repetitive token burning.";
            return false;
        }

        _state.SeenErrorHashes.Add(errorHash);

        // Check Attempt ceiling
        if (_state.AttemptCount >= _options.MaxAttempts)
        {
            _state.ExceededAttemptBudget = true;
            haltReason = $"Self-healing reached maximum repair budget ({_options.MaxAttempts} attempts).";
            return false;
        }

        // Check Time budget
        if (_stopwatch.Elapsed.TotalSeconds >= _options.MaxCumulativeTimeSeconds)
        {
            _state.ExceededTimeBudget = true;
            haltReason = $"Self-healing exceeded cumulative execution budget ({_options.MaxCumulativeTimeSeconds}s).";
            return false;
        }

        return false;
    }

    public bool ShouldContinue(out string? stopReason)
    {
        if (_state.HasRepetitionLoop)
        {
            stopReason = "Halted due to repeated compiler error.";
            return false;
        }

        if (_state.ExceededAttemptBudget)
        {
            stopReason = $"Exceeded max repair attempts ({_options.MaxAttempts}).";
            return false;
        }

        if (_state.ExceededTimeBudget)
        {
            stopReason = $"Exceeded maximum time budget ({_options.MaxCumulativeTimeSeconds}s).";
            return false;
        }

        stopReason = null;
        return true;
    }

    public async Task<bool> RollbackToPreRepairAsync(IWorkspaceCheckpointService checkpointService, string workspacePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_preRepairCheckpointId))
        {
            return await checkpointService.RollbackLatestAsync(workspacePath, ct);
        }

        return await checkpointService.RollbackCheckpointAsync(_preRepairCheckpointId, ct);
    }
}
