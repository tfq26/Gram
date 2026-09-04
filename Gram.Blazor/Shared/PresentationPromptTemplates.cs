namespace Gram.Blazor.Shared;

public static class PresentationPromptTemplates
{
    public const string QuadraticFormulaTitle = "The Quadratic Formula";
    public const string QuadraticFormulaTitleSlide = """
        Create a presentation about the quadratic formula.
        Generate exactly one title slide only.
        The slide title must be "The Quadratic Formula".
        Use the subtitle "A practical way to solve ax² + bx + c = 0".
        Include no body bullet points, no second slide, and no unsupported historical claims.
        Compose it like a normal professional presentation title slide: clear title hierarchy,
        restrained subtitle, generous whitespace, and one centered formula as the visual focal point.
        Do not use dashboard cards, UI panels, dense grids, app chrome, decorative badges, or a
        generic AI-generated landing page style.
        Follow the conventions of a default PowerPoint Title Slide layout: a 16:9 canvas,
        one dominant title placeholder, one smaller subtitle directly beneath it, balanced
        vertical centering, restrained typography, and a plain light background. Use no top bar,
        navigation, fullscreen control, browser controls, footer toolbar, logo, or application chrome.
        Return only the required JSON object.
        """;
}
