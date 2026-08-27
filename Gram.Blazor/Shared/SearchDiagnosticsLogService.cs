using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed class SearchDiagnosticLogEntry
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Query { get; init; } = "";
    public string ModelId { get; init; } = "";
    public long SearchMilliseconds { get; init; }
    public long ModelMilliseconds { get; init; }
    public long TotalMilliseconds { get; init; }
    public int ResultCount { get; init; }
    public int ResultsWithSnippets { get; init; }
    public int RelevantResultCount { get; init; }
    public int SourceTypeCount { get; init; }
    public int FetchedPageCount { get; init; }
    public int EstimatedContextTokens { get; init; }
    public decimal? ProviderEstimatedCostUsd { get; init; }
    public bool ProviderCostIsEstimate { get; init; }
    public string Provider { get; init; } = "";
    public bool GroundingReady { get; init; }
    public bool AnswerUsesSources { get; init; }
    public bool? CitationValidationPassed { get; init; }
    public int MaterialClaimCount { get; init; }
    public int CitedMaterialClaimCount { get; init; }
    public int InvalidCitationCount { get; init; }
    public double CitationCoverage { get; init; }
    public int PrimarySourceCount { get; init; }
    public int GroundingContextCharacters { get; init; }
    public IReadOnlyDictionary<string, int>? ProviderUnits { get; init; }
    public string TopicCategory { get; init; } = "general";
    public int SearchQueryCount { get; init; }
    public IReadOnlyList<string>? PrimaryDomains { get; init; }
    public IReadOnlyList<string> MissingCoverage { get; init; } = [];
    public string? Error { get; init; }
    public string Answer { get; init; } = "";
    public string GroundingContext { get; init; } = "";
    public IReadOnlyList<SearchDiagnosticSourceLog> Sources { get; init; } = [];
}

public sealed record SearchDiagnosticSourceLog(
    string Title,
    string Url,
    string Snippet,
    string Source,
    bool Relevant);

public sealed class SearchDiagnosticsLogService
{
    private readonly string _path;
    private readonly ILogger<SearchDiagnosticsLogService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string LogPath => _path;

    public SearchDiagnosticsLogService(StorageService storage, ILogger<SearchDiagnosticsLogService> logger)
    {
        _path = storage.GetFilePath("search_diagnostics.jsonl");
        _logger = logger;
    }

    public async Task AppendAsync(SearchDiagnosticLogEntry entry, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            await File.AppendAllTextAsync(_path, line, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist search diagnostic {RunId}.", entry.RunId);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
