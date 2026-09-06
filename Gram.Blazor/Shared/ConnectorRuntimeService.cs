using System.Text.Json;

namespace Gram.Blazor.Shared;

public enum ConnectorRuntimeStatus { Inactive, Starting, Healthy, Unhealthy, Disabled }
public sealed record ConnectorRuntimeSnapshot(string ConnectorId, string PackageId, string Version, ConnectorRuntimeStatus Status, DateTimeOffset UpdatedAt, string? Error = null, string OwnerId = "default");

public interface IConnectorRuntimeService
{
    Task<ConnectorRuntimeSnapshot> ActivateAsync(string packageId, CancellationToken cancellationToken = default, string? ownerId = null);
    Task<ConnectorRuntimeSnapshot> HealthAsync(string connectorId, CancellationToken cancellationToken = default, string? ownerId = null);
    Task<bool> DisableAsync(string connectorId, CancellationToken cancellationToken = default, string? ownerId = null);
    Task<ConnectorRuntimeSnapshot> RollbackAsync(string connectorId, CancellationToken cancellationToken = default, string? ownerId = null);
    IReadOnlyList<ConnectorRuntimeSnapshot> List(string? ownerId = null);
}

public sealed class ConnectorRuntimeService : IConnectorRuntimeService
{
    private readonly IConnectorPackageService _packages;
    private readonly IConnectorRegistry _registry;
    private readonly IConnectorHostClient _host;
    private readonly Dictionary<string, ConnectorRuntimeSnapshot> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly string? _persistencePath;

    public ConnectorRuntimeService(IConnectorPackageService packages, IConnectorRegistry registry, IConnectorHostClient host, StorageService? storage = null)
    {
        _packages = packages;
        _registry = registry;
        _host = host;
        _persistencePath = storage?.GetFilePath("connector_runtime.json");
        LoadState();
    }

    public async Task<ConnectorRuntimeSnapshot> ActivateAsync(string packageId, CancellationToken cancellationToken = default, string? ownerId = null)
    {
        var package = await _packages.GetAsync(packageId, cancellationToken, ownerId) ?? throw new KeyNotFoundException("Connector package was not found.");
        if (package.Status != ConnectorPackageStatus.Installed || !package.ExplicitlyApproved) throw new InvalidOperationException("Only explicitly approved installed packages can be activated.");
        if (package.Manifest.ExecutionPolicy is null) throw new InvalidOperationException("Connector execution policy is missing.");
        var compiledAssembly = Directory.EnumerateFiles(package.PackagePath, "*.dll", SearchOption.AllDirectories).FirstOrDefault(path => Path.GetFileName(path).Contains("Connector.Validation", StringComparison.OrdinalIgnoreCase));
        if (compiledAssembly == null) throw new InvalidOperationException("Compiled connector artifact was not found.");

        // Register only a manifest proxy. Generated assemblies are never loaded into
        // the Gram process; a separate connector host must execute provider code.
        _registry.Register(new IsolatedConnectorProxy(package, _host));
        var snapshot = new ConnectorRuntimeSnapshot(package.Manifest.Id, package.Id, package.Version, ConnectorRuntimeStatus.Healthy, DateTimeOffset.UtcNow, OwnerId: ownerId ?? package.OwnerId);
        lock (_sync)
        {
            _active[package.Manifest.Id] = snapshot;
            PersistStateLocked();
        }
        return snapshot;
    }

    public Task<ConnectorRuntimeSnapshot> HealthAsync(string connectorId, CancellationToken cancellationToken = default, string? ownerId = null)
    {
        lock (_sync)
        {
            if (_active.TryGetValue(connectorId, out var state) && (ownerId == null || state.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase))) return Task.FromResult(state);
        }
        return Task.FromResult(new ConnectorRuntimeSnapshot(connectorId, "", "", ConnectorRuntimeStatus.Inactive, DateTimeOffset.UtcNow));
    }

    public Task<bool> DisableAsync(string connectorId, CancellationToken cancellationToken = default, string? ownerId = null)
    {
        lock (_sync)
        {
            if (ownerId != null && _active.TryGetValue(connectorId, out var current) && !current.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(false);
        }
        var removed = _registry.Unregister(connectorId);
        lock (_sync)
        {
            if (_active.TryGetValue(connectorId, out var state)) _active[connectorId] = state with { Status = ConnectorRuntimeStatus.Disabled, UpdatedAt = DateTimeOffset.UtcNow };
            PersistStateLocked();
        }
        return Task.FromResult(removed);
    }

    public async Task<ConnectorRuntimeSnapshot> RollbackAsync(string connectorId, CancellationToken cancellationToken = default, string? ownerId = null)
    {
        var candidates = (await _packages.ListAsync(cancellationToken, ownerId)).Where(x => x.Manifest.Id.Equals(connectorId, StringComparison.OrdinalIgnoreCase) && x.Status == ConnectorPackageStatus.Installed && x.ExplicitlyApproved).OrderByDescending(x => x.UpdatedAt).ToList();
        if (candidates.Count < 2) throw new InvalidOperationException("No previous installed connector version is available.");
        await DisableAsync(connectorId, cancellationToken, ownerId);
        return await ActivateAsync(candidates[1].Id, cancellationToken, ownerId);
    }

    public IReadOnlyList<ConnectorRuntimeSnapshot> List(string? ownerId = null) { lock (_sync) return _active.Values.Where(x => ownerId == null || x.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.ConnectorId).ToList(); }

    private void LoadState()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath)) return;
        try
        {
            var snapshots = JsonSerializer.Deserialize<List<ConnectorRuntimeSnapshot>>(File.ReadAllText(_persistencePath), BackendJson.Options) ?? [];
            foreach (var snapshot in snapshots)
            {
                _active[snapshot.ConnectorId] = snapshot.Status is ConnectorRuntimeStatus.Healthy or ConnectorRuntimeStatus.Starting
                    ? snapshot with { Status = ConnectorRuntimeStatus.Unhealthy, Error = "Runtime state was restored after restart; reactivation is required.", UpdatedAt = DateTimeOffset.UtcNow }
                    : snapshot;
            }
        }
        catch
        {
            _active.Clear();
        }
    }

    private void PersistStateLocked()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath)) return;
        try
        {
            var temporaryPath = _persistencePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_active.Values, BackendJson.Options));
            File.Move(temporaryPath, _persistencePath, true);
        }
        catch
        {
            // Runtime state remains available in memory if local persistence fails.
        }
    }

    private sealed class IsolatedConnectorProxy : IConnectedConnector
    {
        private readonly ConnectorPackageRecord _package;
        private readonly IConnectorHostClient _host;
        public IsolatedConnectorProxy(ConnectorPackageRecord package, IConnectorHostClient host) { _package = package; _host = host; }
        public ConnectorDescriptor Descriptor => new(_package.Manifest.Id, _package.Manifest.Name, ConnectorTransport.LocalAgent, _package.Manifest.Description, true);
        public IReadOnlyList<ConnectorAction> Actions => _package.Manifest.Actions.Select(a => new ConnectorAction(a.Id, _package.Manifest.Id, a.Name, a.Description, a.RiskLevel, RequiresApproval: a.RequiresApproval, RequiredScopes: a.RequiredScopes)).ToList();
        public async Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default)
        {
            return await _host.ExecuteAsync(new ConnectorHostExecuteRequest(_package.Id, request), cancellationToken);
        }
    }
}
