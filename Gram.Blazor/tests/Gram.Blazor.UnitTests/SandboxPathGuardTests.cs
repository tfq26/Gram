using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class SandboxPathGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gram_path_guard_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolvesPathsInsideSandbox()
    {
        Directory.CreateDirectory(_root);
        Assert.True(SandboxPathGuard.TryResolve(_root, "src/Program.cs", out var path, out _));
        Assert.StartsWith(Path.GetFullPath(_root), path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("/tmp/outside.txt")]
    public void RejectsPathsOutsideSandbox(string path)
    {
        Assert.False(SandboxPathGuard.TryResolve(_root, path, out _, out var error));
        Assert.Contains("escape", error!, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
