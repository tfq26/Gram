using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed record PresentationAssemblyInput(
    string ProjectId,
    string Title,
    string Audience,
    string CommunicationJob,
    string DesignSystem,
    IReadOnlyList<PresentationSectionDraft> Sections,
    IReadOnlyList<ContentAsset> Assets);

public sealed record PresentationSectionDraft(
    string SectionId,
    int Version,
    string Title,
    string NarrativeJob,
    string ContentJson,
    string VisualBrief,
    string? WorkerSummary);

public sealed record PresentationGenerationLogEntry(
    DateTimeOffset Timestamp,
    string Stage,
    string Message,
    string? ProjectId = null,
    string? SectionId = null,
    string? TaskId = null,
    string? Exception = null);

/// <summary>
/// Coordinates presentation work by capability and section ownership. The service
/// deliberately does not make the main agent rewrite every section when one changes.
/// </summary>
public sealed class PresentationGenerationService : IDisposable
{
    private readonly ISubagentEngine _subagentEngine;
    private readonly StorageService _storage;
    private readonly ILogger<PresentationGenerationService> _logger;
    private readonly PresentationImageAssetService? _imageAssetService;
    private readonly HtmlPresentationRenderer? _htmlRenderer;
    private readonly ConcurrentQueue<PresentationGenerationLogEntry> _logs = new();
    private readonly object _logFileLock = new();
    private readonly string _logPath;
    private readonly string _projectsDirectory;
    private readonly Dictionary<string, ContentProject> _projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _workerThrottle = new(2, 2);

    public event Action<ContentProject>? ProjectUpdated;

    public static bool IsPresentationRequest(string? prompt, IReadOnlyList<MentionReference>? mentions = null)
    {
        if (mentions != null && mentions.Any(m => m.Kind == MentionKind.Presentation || m.Target.Equals("presentation", StringComparison.OrdinalIgnoreCase) || m.Id.Equals("presentation", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(prompt)) return false;
        var text = prompt.Trim().ToLowerInvariant();

        if (text.StartsWith("@presentation") || text.Contains("@presentation")) return true;

        var explicitCreationVerbs = new[] { "create", "make", "generate", "build", "design", "draft", "prepare", "compose", "assemble", "produce", "write" };
        var presentationNouns = new[] { "presentation", "powerpoint", "power point", "slide deck", "slides", "pitch deck", "deck", "pptx" };

        foreach (var noun in presentationNouns)
        {
            if (text.Contains(noun))
            {
                foreach (var verb in explicitCreationVerbs)
                {
                    if (text.Contains($"{verb} a {noun}") ||
                        text.Contains($"{verb} the {noun}") ||
                        text.Contains($"{verb} me a {noun}") ||
                        text.Contains($"{verb} {noun}") ||
                        text.Contains($"{verb} us a {noun}") ||
                        text.Contains($"{verb} our {noun}"))
                    {
                        return true;
                    }
                }
            }
        }

        if (text.StartsWith("presentation on") || text.StartsWith("presentation about") ||
            text.StartsWith("presentation for") || text.StartsWith("slides on") ||
            text.StartsWith("slides about") || text.StartsWith("slides for") ||
            text.StartsWith("slide deck on") || text.StartsWith("slide deck about") ||
            text.StartsWith("slide deck for") || text.StartsWith("deck on") ||
            text.StartsWith("deck about") || text.StartsWith("deck for"))
        {
            return true;
        }

        return false;
    }

    public PresentationGenerationService(
        ISubagentEngine subagentEngine,
        StorageService storage,
        ILogger<PresentationGenerationService> logger,
        PresentationImageAssetService? imageAssetService = null,
        HtmlPresentationRenderer? htmlRenderer = null)
    {
        _subagentEngine = subagentEngine;
        _storage = storage;
        _logger = logger;
        _imageAssetService = imageAssetService;
        _htmlRenderer = htmlRenderer;
        _logPath = _storage.GetFilePath("presentation_generation.jsonl");
        _projectsDirectory = Path.Combine(_storage.DataDirectory, "presentations");
        Directory.CreateDirectory(_projectsDirectory);
        LoadSavedProjects();
        LoadRecentLogs();
        _subagentEngine.OnTaskUpdated += OnTaskUpdated;
        Log("service.initialized", "Presentation generation service initialized.");
    }

    public IReadOnlyList<PresentationGenerationLogEntry> GetLogs(string? projectId = null, int maxEntries = 100)
    {
        var entries = _logs
            .Where(entry => string.IsNullOrWhiteSpace(projectId)
                || (!string.IsNullOrWhiteSpace(entry.ProjectId) && entry.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(entry => entry.Timestamp)
            .Take(Math.Clamp(maxEntries, 1, 250))
            .ToList();
        return entries;
    }

    public ContentProject? GetProject(string projectId) =>
        _projects.TryGetValue(projectId, out var project) ? project : null;

    public IReadOnlyList<ContentProject> GetProjects(string parentConversationId) =>
        _projects.Values
            .Where(project => project.ParentConversationId.Equals(parentConversationId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(project => project.UpdatedAt)
            .ToList();

    public void NotifyProjectUpdated(ContentProject project)
    {
        PersistProject(project);
        ProjectUpdated?.Invoke(project);
    }

    public void PersistProject(ContentProject project)
    {
        try
        {
            lock (_projects)
            {
                var path = Path.Combine(_projectsDirectory, $"{project.Id}.json");
                File.WriteAllText(path, JsonSerializer.Serialize(project, BackendJson.Options));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist presentation project {ProjectId}.", project.Id);
        }
    }

    private void LoadSavedProjects()
    {
        try
        {
            if (!Directory.Exists(_projectsDirectory)) return;
            foreach (var file in Directory.GetFiles(_projectsDirectory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var project = JsonSerializer.Deserialize<ContentProject>(json, BackendJson.Options);
                    if (project is not null)
                    {
                        _projects[project.Id] = project;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize presentation project from {Path}.", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load saved presentation projects from {Dir}.", _projectsDirectory);
        }
    }

    public Task<ContentProject> CreatePresentationAsync(
        PresentationBrief brief,
        string parentConversationId,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var project = new ContentProject
        {
            ParentConversationId = parentConversationId,
            Type = ContentProjectType.Presentation,
            Title = brief.Title.Trim(),
            Audience = brief.Audience.Trim(),
            CommunicationJob = brief.CommunicationJob.Trim(),
            DesignSystem = brief.DesignSystem.Trim(),
            Status = ContentProjectStatus.Generating
        };
        Log("project.created", $"Presentation project created with target slide count {brief.TargetSlideCount}.", project.Id);

        var targetCount = brief.TargetSlideCount > 0 ? brief.TargetSlideCount : 1;
        var sectionBriefs = brief.Sections.Count > 0
            ? brief.Sections.Take(targetCount).ToList()
            : CreateDefaultSectionPlan(targetCount);

        for (var index = 0; index < sectionBriefs.Count; index++)
        {
            var section = sectionBriefs[index];
            project.Sections.Add(new ContentSection
            {
                Order = index + 1,
                Title = section.Title.Trim(),
                NarrativeJob = section.NarrativeJob.Trim(),
                OwnerCapability = string.IsNullOrWhiteSpace(section.OwnerCapability) ? "content-generation" : section.OwnerCapability.Trim(),
                Dependencies = section.Dependencies.ToList()
            });
        }

        _projects[project.Id] = project;
        Log("project.sections_planned", $"Planned {project.Sections.Count} section(s).", project.Id);
        NotifyProjectUpdated(project);

        // Queue the section workers and return the project immediately. Dependency
        // chains continue in the background while the main agent remains responsive.
        foreach (var section in project.Sections)
        {
            Log("section.queued", $"Queueing section '{section.Title}' with capability '{section.OwnerCapability}'.", project.Id, section.Id);
            _ = QueueSectionAsync(project, section, workspacePath, cancellationToken);
        }

        return Task.FromResult(project);
    }

    public async Task<SubagentTask?> ReviseSectionAsync(
        string projectId,
        string sectionId,
        string feedback,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        if (!_projects.TryGetValue(projectId, out var project)) return null;
        var section = project.Sections.FirstOrDefault(item => item.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase));
        if (section is null) return null;
        Log("section.revision_requested", $"Revision requested: {feedback.Trim().Length} feedback character(s).", projectId, sectionId);

        section.Version++;
        section.Status = ContentSectionStatus.Generating;
        section.LatestFeedback = feedback.Trim();
        project.Revision++;
        project.Status = ContentProjectStatus.Generating;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        NotifyProjectUpdated(project);

        return await StartSectionAsync(project, section, workspacePath, cancellationToken);
    }

    public PresentationAssemblyInput? GetAssemblyInput(string projectId)
    {
        if (!_projects.TryGetValue(projectId, out var project))
        {
            Log("assembly.project_missing", "Assembly requested for an unknown project.", projectId);
            return null;
        }
        if (project.Sections.Any(section => section.Status != ContentSectionStatus.Ready))
        {
            Log("assembly.not_ready", "Assembly requested before every section was ready.", projectId);
            return null;
        }
        Log("assembly.ready", "All section outputs passed validation and are ready for assembly.", projectId);

        return new PresentationAssemblyInput(
            project.Id,
            project.Title,
            project.Audience,
            project.CommunicationJob,
            project.DesignSystem,
            project.Sections.OrderBy(section => section.Order).Select(section =>
                new PresentationSectionDraft(
                    section.Id,
                    section.Version,
                    section.Title,
                    section.NarrativeJob,
                    section.ContentJson,
                    section.VisualBrief,
                    section.OwnerTaskId is null ? null : _subagentEngine.GetTask(section.OwnerTaskId)?.ResultSummary)).ToList(),
            project.Assets.ToList());
    }

    public async Task<string?> AssembleHtmlAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (_htmlRenderer is null) return null;
        var input = GetAssemblyInput(projectId);
        if (input is null) return null;
        var path = await _htmlRenderer.RenderAsync(input, cancellationToken);
        if (_projects.TryGetValue(projectId, out var project))
        {
            project.FinalArtifactPath = path;
            project.Status = ContentProjectStatus.Assembled;
            project.UpdatedAt = DateTimeOffset.UtcNow;
            Log("assembly.html_ready", $"Interactive HTML presentation assembled at {path}.", project.Id);
            NotifyProjectUpdated(project);
        }
        return path;
    }

    private async Task<SubagentTask> StartSectionAsync(
        ContentProject project,
        ContentSection section,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        if (section.Dependencies.Count > 0)
        {
            Log("section.dependencies_waiting", $"Waiting for {section.Dependencies.Count} section dependency(ies).", project.Id, section.Id);
        }
        await WaitForDependenciesAsync(project, section, cancellationToken);
        section.Status = ContentSectionStatus.Queued;
        Log("section.dependencies_ready", "Section dependencies are ready.", project.Id, section.Id);
        var task = await _subagentEngine.SpawnTaskAsync(new SubagentTaskSpec
        {
            ParentConversationId = project.ParentConversationId,
            Capability = section.OwnerCapability,
            TaskType = "presentation.section",
            SectionId = section.Id,
            OutputFormat = "presentation-section-json",
            ModelId = "gram-q-plus",
            Prompt = BuildSectionPrompt(project, section),
            WorkspacePath = workspacePath,
            UseSandbox = false,
            Dependencies = section.Dependencies.ToList()
        }, cancellationToken);

        section.OwnerTaskId = task.Id;
        section.OwnerProfileId = task.SubagentName;
        section.Status = ContentSectionStatus.Generating;
        Log("worker.spawned", $"Worker '{task.SubagentName}' spawned for capability '{section.OwnerCapability}'.", project.Id, section.Id, task.Id);
        section.UpdatedAt = DateTimeOffset.UtcNow;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        NotifyProjectUpdated(project);
        return task;
    }

    private async Task QueueSectionAsync(
        ContentProject project,
        ContentSection section,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        var attempts = 0;
        const int maxAttempts = 3;
        while (attempts < maxAttempts)
        {
            attempts++;
            try
            {
                await _workerThrottle.WaitAsync(cancellationToken);
                try
                {
                    // Stagger workers to prevent simultaneous burst rate limits
                    await Task.Delay(Random.Shared.Next(250, 600), cancellationToken);
                    await StartSectionAsync(project, section, workspacePath, cancellationToken);
                    return;
                }
                finally
                {
                    _workerThrottle.Release();
                }
            }
            catch (OperationCanceledException)
            {
                section.Status = ContentSectionStatus.Failed;
                project.Status = ContentProjectStatus.Failed;
                NotifyProjectUpdated(project);
                Log("section.failed", "Section worker was cancelled.", project.Id, section.Id);
                return;
            }
            catch (Exception ex)
            {
                if (attempts >= maxAttempts)
                {
                    section.Status = ContentSectionStatus.Failed;
                    section.LatestFeedback = ex.Message;
                    project.Status = ContentProjectStatus.Failed;
                    NotifyProjectUpdated(project);
                    Log("section.failed", "Section worker could not be completed.", project.Id, section.Id, exception: ex);
                    return;
                }
                Log("section.retry", $"Retrying section '{section.Title}' after transient error (attempt {attempts}/{maxAttempts}): {ex.Message}", project.Id, section.Id);
                await Task.Delay(1800 * attempts, cancellationToken);
            }
        }
    }

    private static async Task WaitForDependenciesAsync(
        ContentProject project,
        ContentSection section,
        CancellationToken cancellationToken)
    {
        if (section.Dependencies.Count == 0) return;

        var dependencies = project.Sections
            .Where(candidate => section.Dependencies.Any(dependency =>
                dependency.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase)
                || dependency.Equals(candidate.Title, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        while (dependencies.Any(dependency => dependency.Status is ContentSectionStatus.Planned or ContentSectionStatus.Queued or ContentSectionStatus.Generating))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken);
        }

        if (dependencies.Any(dependency => dependency.Status is ContentSectionStatus.Failed or ContentSectionStatus.NeedsRevision))
        {
            throw new InvalidOperationException($"A dependency for section '{section.Title}' is not ready.");
        }
    }

    private void OnTaskUpdated(SubagentTask task)
    {
        if (string.IsNullOrWhiteSpace(task.SectionId)) return;
        var project = _projects.Values.FirstOrDefault(candidate =>
            candidate.Sections.Any(section => section.OwnerTaskId == task.Id));
        if (project is null) return;

        var section = project.Sections.First(item => item.OwnerTaskId == task.Id);
        Log(
            "worker.status",
            $"Worker status changed to {task.Status}.",
            project.Id,
            section.Id,
            task.Id,
            exception: string.IsNullOrWhiteSpace(task.ErrorMessage) ? null : new InvalidOperationException(task.ErrorMessage));
        if (task.Status == SubagentTaskStatus.Completed)
        {
            if (TryValidateSectionOutput(task.ResultSummary, out var contentJson, out var visualBrief))
            {
                section.Status = ContentSectionStatus.Ready;
                section.ContentJson = contentJson;
                section.VisualBrief = visualBrief;
                Log("section.validated", "Worker output passed the presentation section schema.", project.Id, section.Id, task.Id);
                _ = AttachVisualAssetAsync(project, section);
            }
            else
            {
                section.Status = ContentSectionStatus.NeedsRevision;
                section.LatestFeedback = "Worker output did not match the presentation section schema. Regenerate this section.";
                Log("section.validation_failed", "Worker output did not match the presentation section schema.", project.Id, section.Id, task.Id);
            }
        }
        else if (task.Status == SubagentTaskStatus.Failed || task.Status == SubagentTaskStatus.Cancelled)
        {
            section.Status = ContentSectionStatus.Failed;
        }
        else
        {
            section.Status = ContentSectionStatus.Generating;
        }

        section.UpdatedAt = DateTimeOffset.UtcNow;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        project.Status = project.Sections.All(item => item.Status == ContentSectionStatus.Ready)
            ? ContentProjectStatus.ReadyForAssembly
            : project.Sections.Any(item => item.Status == ContentSectionStatus.NeedsRevision)
                ? ContentProjectStatus.NeedsRevision
            : project.Sections.Any(item => item.Status == ContentSectionStatus.Failed)
                ? ContentProjectStatus.Failed
                : ContentProjectStatus.Generating;
        NotifyProjectUpdated(project);
        if (project.Status == ContentProjectStatus.ReadyForAssembly)
        {
            Log("project.ready_for_assembly", "All presentation sections are ready for assembly.", project.Id);
            _ = AssembleHtmlAsync(project.Id);
        }
    }

    private async Task AttachVisualAssetAsync(ContentProject project, ContentSection section)
    {
        if (_imageAssetService is null || string.IsNullOrWhiteSpace(section.VisualBrief)) return;
        try
        {
            Log("asset.search_started", $"Searching Wikimedia Commons for section visual: {section.VisualBrief}", project.Id, section.Id);
            var cached = await _imageAssetService.SearchAndCacheAsync(section.VisualBrief, project.Id, section.Id);
            if (cached is null)
            {
                Log("asset.search_empty", "No suitable licensed image was found for this section.", project.Id, section.Id);
                return;
            }

            var source = new ContentSource
            {
                Label = cached.Source.Title,
                Url = cached.Source.SourcePageUrl,
                Claim = $"Image license: {cached.Source.License ?? "See source page"}; creator: {cached.Source.Creator ?? "not specified"}",
                RetrievedAt = DateTimeOffset.UtcNow.ToString("O")
            };
            cached.Asset.SourceId = source.Id;
            project.Sources.Add(source);
            project.Assets.Add(cached.Asset);
            Log("asset.cached", $"Cached image '{cached.Source.Title}' with license '{cached.Source.License ?? "unspecified"}'.", project.Id, section.Id);
            NotifyProjectUpdated(project);
            if (project.Status == ContentProjectStatus.ReadyForAssembly) _ = AssembleHtmlAsync(project.Id);
        }
        catch (Exception ex)
        {
            Log("asset.search_failed", "Image search failed; continuing without a remote image.", project.Id, section.Id, exception: ex);
        }
    }

    private static string BuildSectionPrompt(ContentProject project, ContentSection section)
    {
        var dependencies = section.Dependencies.Count == 0 ? "none" : string.Join(", ", section.Dependencies);
        var feedback = string.IsNullOrWhiteSpace(section.LatestFeedback) ? "none" : section.LatestFeedback;
        return $"""
            Create only section {section.Order} for the presentation "{project.Title}".
            Audience: {project.Audience}
            Communication job: {project.CommunicationJob}
            Design system: {project.DesignSystem}
            Section title: {section.Title}
            Section narrative job: {section.NarrativeJob}
            Dependencies: {dependencies}
            Revision feedback: {feedback}

            Return JSON with exactly these fields: slideType, takeawayTitle, subtitle, bodyPoints,
            visualBrief, layoutId, speakerNotes, and sourceIds.

            Use claims only when supported by provided sources or explicit user material. Keep the
            slide visually focused; do not create a generic bullet dump. Do not write other sections.
            """;
    }

    private static List<PresentationSectionBrief> CreateDefaultSectionPlan(int targetSlideCount)
    {
        if (targetSlideCount <= 1)
        {
            return
            [
                new()
                {
                    Title = "Overview",
                    NarrativeJob = "Establish the central takeaway, key insights, and actionable conclusion.",
                    OwnerCapability = "writing"
                }
            ];
        }

        var count = Math.Clamp(targetSlideCount, 1, 12);
        var plan = new List<PresentationSectionBrief>
        {
            new() { Title = "Opening", NarrativeJob = "Establish the audience question, stakes, and central takeaway.", OwnerCapability = "writing" },
            new() { Title = "Context", NarrativeJob = "Explain the situation and the forces shaping it.", OwnerCapability = "writing" },
            new() { Title = "Evidence", NarrativeJob = "Present the strongest evidence or data behind the takeaway.", OwnerCapability = "data-analysis", Dependencies = ["Context"] },
            new() { Title = "Implications", NarrativeJob = "Show what the evidence means for this audience.", OwnerCapability = "writing", Dependencies = ["Evidence"] },
            new() { Title = "Action", NarrativeJob = "Close with the practical decision, recommendation, or next step.", OwnerCapability = "writing", Dependencies = ["Implications"] }
        };

        if (count < plan.Count)
        {
            return plan.Take(count).ToList();
        }

        while (plan.Count < count)
        {
            plan.Insert(plan.Count - 1, new PresentationSectionBrief
            {
                Title = $"Supporting idea {plan.Count - 3}",
                NarrativeJob = "Add one important supporting idea that advances the narrative.",
                OwnerCapability = "visual-design",
                Dependencies = ["Context"]
            });
        }
        return plan;
    }

    private static bool TryValidateSectionOutput(string? resultSummary, out string contentJson, out string visualBrief)
    {
        contentJson = "";
        visualBrief = "";
        if (string.IsNullOrWhiteSpace(resultSummary)) return false;

        var candidate = resultSummary.Trim();
        if (candidate.StartsWith("```") && candidate.EndsWith("```"))
        {
            var firstLineEnd = candidate.IndexOf('\n');
            candidate = firstLineEnd >= 0
                ? candidate[(firstLineEnd + 1)..^3].Trim()
                : candidate.Trim('`', ' ', '\r', '\n');
        }

        try
        {
            using var document = JsonDocument.Parse(candidate);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var title = root.TryGetProperty("takeawayTitle", out var titleElement) ? titleElement.GetString() : null;
            var visual = root.TryGetProperty("visualBrief", out var visualElement) ? visualElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(visual)) return false;

            contentJson = root.GetRawText();
            visualBrief = visual.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _subagentEngine.OnTaskUpdated -= OnTaskUpdated;
    }

    private void Log(string stage, string message, string? projectId = null, string? sectionId = null, string? taskId = null, Exception? exception = null)
    {
        var entry = new PresentationGenerationLogEntry(
            DateTimeOffset.UtcNow,
            stage,
            message,
            projectId,
            sectionId,
            taskId,
            exception?.ToString());
        _logs.Enqueue(entry);
        while (_logs.Count > 250 && _logs.TryDequeue(out _)) { }
        _logger.LogInformation("Presentation {Stage}: {Message} Project={ProjectId} Section={SectionId} Task={TaskId}", stage, message, projectId, sectionId, taskId);
        if (exception is not null) _logger.LogError(exception, "Presentation {Stage} failed", stage);
        PersistLog(entry);
    }

    private void PersistLog(PresentationGenerationLogEntry entry)
    {
        try
        {
            lock (_logFileLock)
            {
                File.AppendAllText(_logPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist presentation generation log entry.");
        }
    }

    private void LoadRecentLogs()
    {
        try
        {
            if (!File.Exists(_logPath)) return;
            foreach (var line in File.ReadLines(_logPath).TakeLast(250))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<PresentationGenerationLogEntry>(line);
                    if (entry is not null) _logs.Enqueue(entry);
                }
                catch (JsonException)
                {
                    // A line may be incomplete if the process was interrupted during
                    // an append. Ignore that record and retain the valid history.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load presentation generation logs.");
        }
    }
}
