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

    [Fact]
    public async Task DependencyGraphExtractsNodesEdgesAndDependencies()
    {
        using var repo = new TemporaryRepository();
        await repo.RunGitAsync("init", "-q", "-b", "main");
        await repo.RunGitAsync("config", "user.email", "gram-test@example.invalid");
        await repo.RunGitAsync("config", "user.name", "Gram Test");

        await File.WriteAllTextAsync(Path.Combine(repo.Path, "IUserRepository.cs"), "public interface IUserRepository { void Save(); }");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "UserService.cs"), "public class UserService { private readonly IUserRepository _repo; public UserService(IUserRepository repo) { _repo = repo; } }");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "UserController.cs"), "public class UserController { private readonly UserService _svc; public UserController(UserService svc) { _svc = svc; } }");
        await repo.RunGitAsync("add", ".");
        await repo.RunGitAsync("commit", "-q", "-m", "chore: graph fixture");

        var service = new RepositoryMapService();
        var graph = await service.BuildDependencyGraphAsync(repo.Path);

        Assert.NotNull(graph);
        Assert.Equal(3, graph!.Nodes.Count);
        Assert.True(graph.TotalEdges >= 2, $"Expected at least 2 edges, found {graph.TotalEdges}");

        // UserService depends on IUserRepository
        Assert.Contains("IUserRepository.cs", graph.Dependencies["UserService.cs"]);
        Assert.Contains("UserService.cs", graph.Dependents["IUserRepository.cs"]);

        // UserController depends on UserService
        Assert.Contains("UserService.cs", graph.Dependencies["UserController.cs"]);
        Assert.Contains("UserController.cs", graph.Dependents["UserService.cs"]);
    }

    [Fact]
    public async Task PageRankAssignsHigherAuthorityToCoreDependencyHubs()
    {
        using var repo = new TemporaryRepository();
        await repo.RunGitAsync("init", "-q", "-b", "main");
        await repo.RunGitAsync("config", "user.email", "gram-test@example.invalid");
        await repo.RunGitAsync("config", "user.name", "Gram Test");

        // CoreHub is referenced by ModuleA, ModuleB, ModuleC
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "CoreHub.cs"), "public interface ICoreHub { void Execute(); }");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "ModuleA.cs"), "public class ModuleA { private readonly ICoreHub _hub; }");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "ModuleB.cs"), "public class ModuleB { private readonly ICoreHub _hub; }");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "ModuleC.cs"), "public class ModuleC { private readonly ICoreHub _hub; }");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "Isolated.cs"), "public class IsolatedLeaf { }");
        await repo.RunGitAsync("add", ".");
        await repo.RunGitAsync("commit", "-q", "-m", "chore: pagerank fixture");

        var service = new RepositoryMapService();
        var graph = await service.BuildDependencyGraphAsync(repo.Path);

        Assert.NotNull(graph);
        Assert.True(graph!.PageRankScores.ContainsKey("CoreHub.cs"));
        Assert.True(graph.PageRankScores.ContainsKey("Isolated.cs"));

        var hubRank = graph.PageRankScores["CoreHub.cs"];
        var leafRank = graph.PageRankScores["Isolated.cs"];

        Assert.True(hubRank > leafRank, $"Expected CoreHub PageRank ({hubRank}) to exceed Isolated ({leafRank})");
    }

    [Fact]
    public async Task NeighborhoodExpansionBoostsDependenciesOfDirectMatches()
    {
        using var repo = new TemporaryRepository();
        await repo.RunGitAsync("init", "-q", "-b", "main");
        await repo.RunGitAsync("config", "user.email", "gram-test@example.invalid");
        await repo.RunGitAsync("config", "user.name", "Gram Test");

        // PaymentGateway is used by CheckoutController
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "PaymentGateway.cs"), "public class PaymentGateway { public void ChargeCard() {} }");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "CheckoutController.cs"), "public class CheckoutController { private readonly PaymentGateway _gateway; public void ProcessCheckout() {} }");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "RandomWidget.cs"), "public class RandomWidget { public void Render() {} }");
        await repo.RunGitAsync("add", ".");
        await repo.RunGitAsync("commit", "-q", "-m", "chore: neighborhood fixture");

        var service = new RepositoryMapService();
        // Query targets CheckoutController directly
        var map = await service.BuildRelevantMapAsync(repo.Path, "ProcessCheckout CheckoutController", 1200);

        Assert.NotNull(map);
        // Both CheckoutController AND its dependency PaymentGateway should appear before RandomWidget
        Assert.Contains("CheckoutController.cs", map);
        Assert.Contains("PaymentGateway.cs", map);
    }

    [Fact]
    public async Task NonGitWorkspace_FallbackCrawler_IndexesFilesSuccessfully()
    {
        // Pure filesystem folder WITHOUT any git initialization
        var tempFolder = Path.Combine(Path.GetTempPath(), $"gram-nongit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempFolder, "ServiceA.cs"), "public class ServiceA { public void Run() {} }");
            var subDir = Path.Combine(tempFolder, "Sub");
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(subDir, "ServiceB.cs"), "public class ServiceB { private readonly ServiceA _a; }");

            var service = new RepositoryMapService();
            var graph = await service.BuildDependencyGraphAsync(tempFolder);

            Assert.NotNull(graph);
            Assert.True(graph!.Nodes.Count >= 2, $"Expected at least 2 nodes, got {graph.Nodes.Count}");
            Assert.Contains(graph.Nodes, n => n.EndsWith("ServiceA.cs"));

            var map = await service.BuildRelevantMapAsync(tempFolder, "ServiceA", 800);
            Assert.NotNull(map);
            Assert.Contains("ServiceA.cs", map);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
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
