namespace Gram.Blazor.Shared;

public enum ContentProjectType
{
    Presentation,
    Document,
    Spreadsheet,
    Pdf
}

public enum ContentSectionStatus
{
    Planned,
    Queued,
    Generating,
    Ready,
    NeedsRevision,
    Failed
}

public enum ContentProjectStatus
{
    Planning,
    Generating,
    ReadyForAssembly,
    Assembled,
    NeedsRevision,
    Failed
}

public sealed class ContentProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string ParentConversationId { get; set; } = "";
    public ContentProjectType Type { get; set; }
    public string Title { get; set; } = "";
    public string Audience { get; set; } = "";
    public string CommunicationJob { get; set; } = "";
    public string DesignSystem { get; set; } = "";
    public int Revision { get; set; } = 1;
    public ContentProjectStatus Status { get; set; } = ContentProjectStatus.Planning;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ContentSection> Sections { get; set; } = [];
    public List<ContentSource> Sources { get; set; } = [];
    public List<ContentAsset> Assets { get; set; } = [];
    public string? FinalArtifactPath { get; set; }
}

public sealed class ContentSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public int Order { get; set; }
    public string Title { get; set; } = "";
    public string NarrativeJob { get; set; } = "";
    public string OwnerCapability { get; set; } = "writing";
    public string? OwnerProfileId { get; set; }
    public string? OwnerTaskId { get; set; }
    public ContentSectionStatus Status { get; set; } = ContentSectionStatus.Planned;
    public int Version { get; set; } = 1;
    public string ContentJson { get; set; } = "";
    public string VisualBrief { get; set; } = "";
    public List<string> Dependencies { get; set; } = [];
    public List<string> SourceIds { get; set; } = [];
    public string? LatestFeedback { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ContentSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Claim { get; set; }
    public string? RetrievedAt { get; set; }
}

public sealed class ContentAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Kind { get; set; } = "image";
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";
    public string? SourceId { get; set; }
    public string AltText { get; set; } = "";
}
