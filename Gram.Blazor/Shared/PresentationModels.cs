namespace Gram.Blazor.Shared;

public sealed class PresentationBrief
{
    public string Title { get; set; } = "Untitled presentation";
    public string Audience { get; set; } = "General audience";
    public string CommunicationJob { get; set; } = "Explain the subject clearly and help the audience understand what to do next.";
    public string DesignSystem { get; set; } = "Gram editorial: restrained, visual, high-contrast, and evidence-led.";
    public int TargetSlideCount { get; set; } = 1;
    public List<PresentationSectionBrief> Sections { get; set; } = [];
}

public sealed class PresentationSectionBrief
{
    public string Title { get; set; } = "";
    public string NarrativeJob { get; set; } = "";
    public string OwnerCapability { get; set; } = "writing";
    public List<string> Dependencies { get; set; } = [];
}

public sealed class PresentationSlideContent
{
    public string SlideType { get; set; } = "content";
    public string TakeawayTitle { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public List<string> BodyPoints { get; set; } = [];
    public string VisualBrief { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string SpeakerNotes { get; set; } = "";
    public List<string> SourceIds { get; set; } = [];
}
