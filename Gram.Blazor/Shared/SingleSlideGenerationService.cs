using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class SingleSlideGenerationService
{
    private readonly IModelGateway _models;
    private readonly HtmlPresentationRenderer _renderer;
    private readonly PresentationTestStore _testStore;

    public SingleSlideGenerationService(IModelGateway models, HtmlPresentationRenderer renderer, PresentationTestStore testStore)
    { _models = models; _renderer = renderer; _testStore = testStore; }

    public async Task<SingleSlideResponse> GenerateAsync(SingleSlideRequest request, CancellationToken cancellationToken = default)
        => await GenerateCoreAsync(request, false, cancellationToken);

    public async Task<SingleSlideResponse> GenerateTestAsync(SingleSlideRequest request, CancellationToken cancellationToken = default)
        => await GenerateCoreAsync(request, true, cancellationToken);

    private async Task<SingleSlideResponse> GenerateCoreAsync(SingleSlideRequest request, bool temporary, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt)) return new(false, Error: "A slide prompt is required.");
        var modelId = string.IsNullOrWhiteSpace(request.ModelId) ? GramModelIds.GramQPlusPlus : request.ModelId;
        var system = "You create one focused, conventional presentation slide. Follow a default PowerPoint Title Slide layout: 16:9 canvas, one dominant title, one smaller subtitle directly beneath it, balanced vertical centering, restrained typography, plain light background, and one intentional central visual. Compose it like a polished human-made presentation, not a dashboard or web-app landing page. Do not generate a top bar, navigation, fullscreen control, browser controls, footer toolbar, logo, application chrome, UI panels, badges, or dense grids. Return only valid JSON with exactly these fields: slideType, takeawayTitle, subtitle, bodyPoints, visualBrief, layoutId, speakerNotes, and sourceIds. bodyPoints must be an array of 0 to 5 short strings. Do not use markdown fences or commentary.";
        var prompt = $"Create one clear, modest presentation slide about: {request.Prompt.Trim()}.\nContext: {(string.IsNullOrWhiteSpace(request.Context) ? "none" : request.Context.Trim())}";
        var output = await CompleteAsync(modelId, system, prompt, cancellationToken);
        if (output.StartsWith("[", StringComparison.Ordinal) && output.Contains("error", StringComparison.OrdinalIgnoreCase))
            return new(false, Error: output);
        if (!TryValidate(output, out var contentJson, out var validationError))
        {
            output = await CompleteAsync(modelId, system, $"Repair this into JSON matching the required schema. Return JSON only.\n{output}", cancellationToken);
            if (output.StartsWith("[", StringComparison.Ordinal) && output.Contains("error", StringComparison.OrdinalIgnoreCase))
                return new(false, Error: output);
            if (!TryValidate(output, out contentJson, out validationError)) return new(false, Error: validationError);
        }

        var projectId = "single-" + Guid.NewGuid().ToString("N")[..10];
        var slideTitle = request.Prompt.Equals(PresentationPromptTemplates.QuadraticFormulaTitleSlide, StringComparison.OrdinalIgnoreCase)
            ? PresentationPromptTemplates.QuadraticFormulaTitle
            : request.Prompt.Trim();
        var input = new PresentationAssemblyInput(projectId, slideTitle, "General audience", "Explain one clear takeaway", "Gram", [new PresentationSectionDraft("slide-1", 1, slideTitle, "Deliver the central takeaway", contentJson, "A clean, focused visual supporting the takeaway", null)], []);
        var outputDirectory = temporary ? _testStore.CreateDirectory(projectId) : null;
        var path = await _renderer.RenderAsync(input, cancellationToken, outputDirectory);
        if (temporary)
        {
            _testStore.Register(projectId, path);
            return new(true, path, contentJson, ViewUrl: $"/api/v1/presentations/test/{projectId}");
        }
        return new(true, path, contentJson);
    }

    private async Task<string> CompleteAsync(string modelId, string system, string prompt, CancellationToken cancellationToken)
    {
        var text = new System.Text.StringBuilder();
        await foreach (var item in _models.StreamAsync(new ModelRequest(modelId, [new Message("user", prompt)], system), cancellationToken))
        {
            if (item.Error is not null) return item.Error;
            if (item.Text is not null) text.Append(item.Text);
        }
        return text.ToString().Trim();
    }

    private static bool TryValidate(string? candidate, out string contentJson, out string error)
    {
        contentJson = ""; error = "The model did not return a valid single-slide JSON response.";
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        var normalized = candidate.Trim();
        if (normalized.StartsWith("```") && normalized.EndsWith("```"))
        {
            var newline = normalized.IndexOf('\n');
            normalized = newline >= 0 ? normalized[(newline + 1)..^3].Trim() : normalized.Trim('`', ' ', '\r', '\n');
        }
        try
        {
            using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;
            var title = root.TryGetProperty("takeawayTitle", out var titleElement) ? titleElement.GetString() : null;
            var visual = root.TryGetProperty("visualBrief", out var visualElement) ? visualElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(visual)) { error = "The slide response needs a takeawayTitle and visualBrief."; return false; }
            contentJson = root.GetRawText(); return true;
        }
        catch (JsonException) { return false; }
    }
}
