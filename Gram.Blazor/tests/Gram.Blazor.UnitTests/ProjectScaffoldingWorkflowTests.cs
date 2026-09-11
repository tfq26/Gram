using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ProjectScaffoldingWorkflowTests
{
    [Theory]
    [InlineData("plan.md", "Project Execution Plan", true)]
    [InlineData("architecture_plan.md", "Architecture", true)]
    [InlineData("scaffolding_plan.markdown", "Scaffolding", false)]
    [InlineData("README.md", "Overall Plan for Database", true)]
    [InlineData("index.html", "Landing Page", false)]
    [InlineData("styles.css", "Styles", false)]
    public void IsPlanArtifact_CorrectlyIdentifiesPlanningArtifacts(string fileName, string title, bool expected)
    {
        var isPlan = fileName.EndsWith("plan.md", StringComparison.OrdinalIgnoreCase) ||
                     title.Contains("plan", StringComparison.OrdinalIgnoreCase);

        Assert.Equal(expected, isPlan);
    }

    [Fact]
    public void ScaffoldingPrompt_FormattedCorrectlyForExecution()
    {
        var plan = new ArtifactInfo
        {
            FileName = "plan.md",
            Title = "Event Booking Service Architecture",
            Language = "markdown",
            Content = "# Event Booking Service\n1. Scaffolding\n2. Implementation\n3. Verification"
        };

        var prompt = $"The project plan '{plan.Title ?? plan.FileName}' has been approved. Please execute the plan: initialize the project structure, scaffold all required files using write_file, and run test verification.";

        Assert.Contains("Event Booking Service Architecture", prompt);
        Assert.Contains("initialize the project structure", prompt);
        Assert.Contains("write_file", prompt);
        Assert.Contains("test verification", prompt);
    }
}
