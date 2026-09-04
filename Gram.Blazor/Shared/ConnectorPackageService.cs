using System.Diagnostics;
using System.Text.Json;

namespace Gram.Blazor.Shared;

public enum ConnectorPackageStatus { Staged, Invalid, Compiled, Installed, Failed }
public sealed record ConnectorPackageRecord(string Id, string Version, ConnectorPackageStatus Status, ConnectorManifest Manifest, string PackagePath, IReadOnlyList<string> Diagnostics, DateTimeOffset UpdatedAt, bool ExplicitlyApproved = false);
public sealed record InstallConnectorPackageRequest(string PackageId, bool Approved);

public interface IConnectorPackageService
{
    Task<ConnectorPackageRecord> StageAsync(ConnectorGenerationPreview preview, CancellationToken cancellationToken = default);
    Task<ConnectorPackageRecord> CompileAsync(string packageId, CancellationToken cancellationToken = default);
    Task<ConnectorPackageRecord> InstallAsync(string packageId, bool approved, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConnectorPackageRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<ConnectorPackageRecord?> GetAsync(string packageId, CancellationToken cancellationToken = default);
}

public sealed class ConnectorPackageService : IConnectorPackageService
{
    private readonly IConnectorSdk _sdk;
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConnectorPackageService(IConnectorSdk sdk)
    {
        _sdk = sdk;
        var data = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
        if (string.IsNullOrWhiteSpace(data)) data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gram");
        _root = Path.Combine(data, "connectors");
        Directory.CreateDirectory(Path.Combine(_root, "staged"));
        Directory.CreateDirectory(Path.Combine(_root, "installed"));
    }

    public async Task<ConnectorPackageRecord> StageAsync(ConnectorGenerationPreview preview, CancellationToken cancellationToken = default)
    {
        var diagnostics = _sdk.Validate(preview.Manifest, preview.AdapterSource).Concat(preview.Warnings).Distinct().ToList();
        var packageId = $"{preview.Manifest.Id}-{preview.Manifest.Version}";
        var path = SafePackagePath("staged", packageId);
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "connector.json"), JsonSerializer.Serialize(preview.Manifest, BackendJson.Options), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(path, "Connector.cs"), preview.AdapterSource, cancellationToken);
        var record = new ConnectorPackageRecord(packageId, preview.Manifest.Version, diagnostics.Count == 0 ? ConnectorPackageStatus.Staged : ConnectorPackageStatus.Invalid, preview.Manifest, path, diagnostics, DateTimeOffset.UtcNow);
        await SaveRecordAsync(record, cancellationToken);
        return record;
    }

    public async Task<ConnectorPackageRecord> CompileAsync(string packageId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var record = await LoadAsync(packageId, cancellationToken) ?? throw new KeyNotFoundException("Connector package was not found.");
            if (record.Status == ConnectorPackageStatus.Invalid) return record;
            var projectPath = Path.Combine(record.PackagePath, "Connector.Validation.csproj");
            var assemblyPath = typeof(IConnectedConnector).Assembly.Location;
            var project = $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><Reference Include=\"Gram.Blazor\"><HintPath>{System.Security.SecurityElement.Escape(assemblyPath)}</HintPath></Reference></ItemGroup></Project>";
            await File.WriteAllTextAsync(projectPath, project, cancellationToken);

            using var process = new Process { StartInfo = new ProcessStartInfo("dotnet", $"build \"{projectPath}\" --nologo --verbosity quiet") { WorkingDirectory = record.PackagePath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { process.Kill(true); } catch { } }
            var output = (await process.StandardOutput.ReadToEndAsync(cancellationToken) + "\n" + await process.StandardError.ReadToEndAsync(cancellationToken)).Trim();
            var diagnostics = process.HasExited && process.ExitCode == 0 ? [] : new List<string> { output.Length > 3000 ? output[..3000] : output };
            var updated = record with { Status = diagnostics.Count == 0 ? ConnectorPackageStatus.Compiled : ConnectorPackageStatus.Failed, Diagnostics = diagnostics, UpdatedAt = DateTimeOffset.UtcNow };
            await SaveRecordAsync(updated, cancellationToken);
            return updated;
        }
        finally { _gate.Release(); }
    }

    public async Task<ConnectorPackageRecord> InstallAsync(string packageId, bool approved, CancellationToken cancellationToken = default)
    {
        if (!approved) throw new UnauthorizedAccessException("Explicit connector installation approval is required.");
        var record = await LoadAsync(packageId, cancellationToken) ?? throw new KeyNotFoundException("Connector package was not found.");
        if (record.Status != ConnectorPackageStatus.Compiled) throw new InvalidOperationException("Only successfully compiled packages can be installed.");
        var installedPath = SafePackagePath("installed", packageId);
        Directory.CreateDirectory(installedPath);
        foreach (var file in new[] { "connector.json", "Connector.cs", "Connector.Validation.csproj" })
        {
            var source = Path.Combine(record.PackagePath, file);
            if (File.Exists(source)) File.Copy(source, Path.Combine(installedPath, file), true);
        }
        var compiled = Directory.EnumerateFiles(record.PackagePath, "Connector.Validation.dll", SearchOption.AllDirectories).FirstOrDefault();
        if (compiled != null) File.Copy(compiled, Path.Combine(installedPath, "Connector.Validation.dll"), true);
        var installed = record with { Status = ConnectorPackageStatus.Installed, PackagePath = installedPath, ExplicitlyApproved = true, UpdatedAt = DateTimeOffset.UtcNow };
        await SaveRecordAsync(installed, cancellationToken);
        return installed;
    }

    public async Task<IReadOnlyList<ConnectorPackageRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var records = new List<ConnectorPackageRecord>();
        foreach (var file in Directory.EnumerateFiles(_root, "package-state.json", SearchOption.AllDirectories))
        {
            try { var value = JsonSerializer.Deserialize<ConnectorPackageRecord>(await File.ReadAllTextAsync(file, cancellationToken), BackendJson.Options); if (value != null) records.Add(value); } catch { }
        }
        return records.GroupBy(x => x.Id).Select(x => x.OrderByDescending(y => y.UpdatedAt).First()).OrderByDescending(x => x.UpdatedAt).ToList();
    }

    public Task<ConnectorPackageRecord?> GetAsync(string packageId, CancellationToken cancellationToken = default) => LoadAsync(packageId, cancellationToken);

    private async Task<ConnectorPackageRecord?> LoadAsync(string packageId, CancellationToken cancellationToken)
    {
        var candidates = new[] { SafePackagePath("installed", packageId), SafePackagePath("staged", packageId) };
        foreach (var path in candidates)
        {
            var file = Path.Combine(path, "package-state.json");
            if (File.Exists(file)) return JsonSerializer.Deserialize<ConnectorPackageRecord>(await File.ReadAllTextAsync(file, cancellationToken), BackendJson.Options);
        }
        return null;
    }

    private async Task SaveRecordAsync(ConnectorPackageRecord record, CancellationToken cancellationToken) => await File.WriteAllTextAsync(Path.Combine(record.PackagePath, "package-state.json"), JsonSerializer.Serialize(record, BackendJson.Options), cancellationToken);
    private string SafePackagePath(string state, string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId) || packageId.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '.' or '_'))) throw new ArgumentException("Invalid connector package ID.");
        return Path.Combine(_root, state, packageId);
    }
}
