using System.Diagnostics;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class RepositoryMapServiceUnitTests
{
    [Fact]
    public async Task RepositoryMapRanksRelevantFilesAndStaysWithinBudget()
    {
        using var repo = new TemporaryRepository();
        await repo.RunGitAsync("init", "-q", "-b", "main");
        await repo.RunGitAsync("config", "user.email", "gram-test@example.invalid");
        await repo.RunGitAsync("config", "user.name", "Gram Test");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "AudioService.cs"), "public class AudioService { public void StartMicrophone() {} }");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "Unrelated.cs"), "public class BillingService { public void Charge() {} }");
        await repo.RunGitAsync("add", ".");
        await repo.RunGitAsync("commit", "-q", "-m", "chore: map fixture");

        var map = await new RepositoryMapService().BuildRelevantMapAsync(repo.Path, "microphone audio", 800);

        Assert.NotNull(map);
        Assert.Contains("AudioService.cs", map);
        Assert.Contains("StartMicrophone", map);
        Assert.True(map!.Length <= 850, $"Map exceeded budget: {map.Length} characters.");
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gram-map-tests-{Guid.NewGuid():N}");

        public TemporaryRepository() => Directory.CreateDirectory(Path);

        public async Task RunGitAsync(params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = Path,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, error);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
