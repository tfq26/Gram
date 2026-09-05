namespace Gram.Blazor.UnitTests;

public sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gram-test-" + Guid.NewGuid().ToString("N"));

    public TemporaryDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
        catch { }
    }
}

public sealed class SimpleHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
