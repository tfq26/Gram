using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class WorkspaceFileServiceUnitTests
{
    [Fact]
    public async Task GetWorkspaceFilesAsync_ReturnsFilesAndIgnoresHiddenAndBin()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gram_ws_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));
            Directory.CreateDirectory(Path.Combine(tempDir, "bin"));
            Directory.CreateDirectory(Path.Combine(tempDir, ".git"));

            await File.WriteAllTextAsync(Path.Combine(tempDir, "src", "Program.cs"), "class Program {}");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "README.md"), "# Test");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "bin", "Program.dll"), "binary");
            await File.WriteAllTextAsync(Path.Combine(tempDir, ".git", "config"), "git");

            var service = new WorkspaceFileService();
            var files = await service.GetWorkspaceFilesAsync(tempDir);

            Assert.Equal(2, files.Count);
            Assert.Contains(files, f => f.RelativePath == "src/Program.cs" && f.FileName == "Program.cs" && f.Extension == ".cs");
            Assert.Contains(files, f => f.RelativePath == "README.md" && f.FileName == "README.md" && f.Extension == ".md");
            Assert.DoesNotContain(files, f => f.RelativePath.Contains("bin"));
            Assert.DoesNotContain(files, f => f.RelativePath.Contains(".git"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task GetWorkspaceFilesAsync_HandlesNonExistentDirectoryGracefully()
    {
        var service = new WorkspaceFileService();
        var files = await service.GetWorkspaceFilesAsync("/non/existent/path/for/gram/test");
        Assert.Empty(files);
    }
}
