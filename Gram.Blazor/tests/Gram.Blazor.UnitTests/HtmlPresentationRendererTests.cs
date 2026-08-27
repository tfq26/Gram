using System.Text.Json;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class HtmlPresentationRendererTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gram_html_presentation_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RenderAsync_CreatesNavigableHtmlFromStructuredSections()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDir"] = _root }).Build();
        var renderer = new HtmlPresentationRenderer(new StorageService(config), NullLogger<HtmlPresentationRenderer>.Instance);
        var input = new PresentationAssemblyInput(
            "project1", "Atlas Report", "Leadership", "Explain the decision", "Gram editorial",
            [new PresentationSectionDraft("s1", 1, "Opening", "Set context", JsonSerializer.Serialize(new PresentationSlideContent { TakeawayTitle = "The takeaway", BodyPoints = ["Evidence point"] }), "A clear visual", null)],
            []);

        var path = await renderer.RenderAsync(input);

        Assert.True(File.Exists(path));
        var html = await File.ReadAllTextAsync(path);
        Assert.Contains("Atlas Report", html);
        Assert.Contains("The takeaway", html);
        Assert.Contains("slide navigation", html, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
