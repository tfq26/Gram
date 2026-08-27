using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed record SummaryMockSource(string Id, string Title, string Published, string Content)
{
    public string Url => $"https://mock.gram/{Id}";
}

public sealed record SummaryTestQuestion(string Id, string Question, string Skill, string ExpectedSourceIds, IReadOnlyList<string>? ExpectedAnswerTerms = null);

public static class SummaryEvaluationCorpus
{
    public static IReadOnlyList<SummaryMockSource> Sources { get; } =
    [
        new("alpha", "Atlas launch brief", "2026-01-12", "Atlas launched its compact battery platform in January 2026. The platform weighs 420 kilograms and targets delivery fleets.") ,
        new("beta", "Atlas independent test", "2026-02-04", "An independent winter test measured 310 miles of range at 55 degrees Fahrenheit, compared with Atlas's advertised 360 miles in ideal conditions."),
        new("gamma", "Northstar fleet report", "2026-02-19", "Northstar tested 100 delivery vehicles for six months. Average energy cost was 8.4 cents per mile, while gasoline vehicles averaged 14.9 cents per mile."),
        new("delta", "Northstar manufacturing update", "2026-03-02", "Northstar delayed its factory opening from June to September 2026 because battery-cell equipment arrived late. The delay did not change the announced vehicle specifications."),
        new("epsilon", "Battery recycling study", "2026-03-18", "The study found that recycling recovered 92 percent of nickel and 87 percent of cobalt from the tested battery chemistry. It did not evaluate lithium recovery."),
        new("zeta", "Charging network survey", "2026-04-07", "Drivers in the survey reported that public charging reliability improved from 81 percent to 89 percent after the network added maintenance staff."),
        new("eta", "Consumer adoption poll", "2026-04-22", "Among 2,000 respondents, 46 percent cited charging access as their biggest concern, 31 percent cited purchase price, and 12 percent cited range."),
        new("theta", "Analyst outlook", "2026-05-15", "The analyst expects fleet adoption to grow faster than private adoption because commercial vehicles follow predictable routes and return to depots. This is a forecast, not a reported result.")
    ];

    public static IReadOnlyList<SummaryTestQuestion> Questions { get; } =
    [
        new("q1", "What weight did Atlas's compact battery platform target?", "direct lookup", "alpha", ["420", "delivery"]),
        new("q2", "What was Atlas's advertised range, and how did the winter test compare?", "comparison", "alpha,beta", ["360", "310", "50"]),
        new("q3", "Which fleet had the lower energy cost per mile, and what was the difference?", "calculation", "gamma", ["8.4", "14.9", "6.5"]),
        new("q4", "Why was Northstar's factory opening delayed, and did the delay change the vehicle specifications?", "causal reasoning", "delta", ["equipment", "September", "unchanged"]),
        new("q5", "What materials did the recycling study recover, at what rates, and what important limitation did it state?", "limitation detection", "epsilon", ["nickel", "92", "cobalt", "87", "lithium"]),
        new("q6", "How did charging reliability change, and what operational change was associated with it?", "synthesis", "zeta", ["81", "89", "maintenance"]),
        new("q7", "Rank the consumer concerns from most common to least common and state the sample size.", "structured extraction", "eta", ["2,000", "46", "31", "12"]),
        new("q8", "What evidence supports the idea that commercial fleets may adopt electric vehicles faster than private buyers? Separate reported evidence from forecasts.", "cross-source reasoning", "gamma,theta", ["predictable", "depots", "forecast"]),
        new("q9", "Across the sources, identify one measured result, one company claim, and one forecast. Do not treat them as equivalent evidence.", "evidence classification", "alpha,beta,theta", ["measured", "claim", "forecast"]),
        new("q10", "Write a concise executive summary of the collection in no more than 120 words, including the most important caveat.", "long-context summarization", "alpha,beta,gamma,delta,epsilon,zeta,eta,theta", ["420", "310", "cost", "recycling", "forecast"])
    ];

    public static string BuildContext() => string.Join("\n\n", Sources.Select(source => $"Source {source.Id}: {source.Title} ({source.Published})\nURL: {source.Url}\n{source.Content}"));
}

public sealed class SummaryDiagnosticsLogService
{
    private readonly string _path;
    private readonly ILogger<SummaryDiagnosticsLogService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    public string LogPath => _path;

    public SummaryDiagnosticsLogService(StorageService storage, ILogger<SummaryDiagnosticsLogService> logger)
    {
        _path = storage.GetFilePath("summary_diagnostics.jsonl");
        _logger = logger;
    }

    public async Task AppendAsync(object entry, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try { await File.AppendAllTextAsync(_path, JsonSerializer.Serialize(entry) + Environment.NewLine, cancellationToken); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not persist summary diagnostics."); }
        finally { _lock.Release(); }
    }
}
