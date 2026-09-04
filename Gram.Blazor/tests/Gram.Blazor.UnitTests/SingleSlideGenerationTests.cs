using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SingleSlideGenerationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gram_single_slide_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GramQCanGenerateAndRenderOneSlideFromStructuredOutput()
    {
        Directory.CreateDirectory(_directory);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GRAM_DATA_DIR"] = _directory }).Build();
        var renderer = new HtmlPresentationRenderer(new StorageService(config), NullLogger<HtmlPresentationRenderer>.Instance);
        var service = new SingleSlideGenerationService(new FakeModelGateway(), renderer, new PresentationTestStore());

        var result = await service.GenerateAsync(new SingleSlideRequest("Why battery storage matters", "gram-q-plus", "Energy reliability and grid resilience."));

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
        Assert.Contains("Grid resilience", result.ContentJson);
        Assert.Contains("Why battery storage matters", await File.ReadAllTextAsync(result.ArtifactPath!));
    }

    [Fact]
    public void QuadraticFormulaPromptIsPersistentAndSingleSlideFocused()
    {
        Assert.Contains("quadratic formula", PresentationPromptTemplates.QuadraticFormulaTitleSlide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly one title slide", PresentationPromptTemplates.QuadraticFormulaTitleSlide, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("gram-q-plus-plus", new SingleSlideRequest().ModelId);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { }
    }

    private sealed class FakeModelGateway : IModelGateway
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ModelStreamEvent("{\"slideType\":\"title\",\"takeawayTitle\":\"Grid resilience\",\"subtitle\":\"Storage makes flexibility available when the grid needs it.\",\"bodyPoints\":[\"Balances variable supply\"],\"visualBrief\":\"A simple battery-to-grid flow\",\"layoutId\":\"hero\",\"speakerNotes\":\"Explain the core tradeoff.\",\"sourceIds\":[]}");
            await Task.CompletedTask;
        }
    }
}
