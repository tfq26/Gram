namespace Gram.Blazor.Shared;

public enum ConnectorRuntimeStatus { Inactive, Starting, Healthy, Unhealthy, Disabled }
public sealed record ConnectorRuntimeSnapshot(string ConnectorId, string PackageId, string Version, ConnectorRuntimeStatus Status, DateTimeOffset UpdatedAt, string? Error = null);

public interface IConnectorRuntimeService
{
    Task<ConnectorRuntimeSnapshot> ActivateAsync(string packageId, CancellationToken cancellationToken = default);
    Task<ConnectorRuntimeSnapshot> HealthAsync(string connectorId, CancellationToken cancellationToken = default);
    Task<bool> DisableAsync(string connectorId, CancellationToken cancellationToken = default);
    Task<ConnectorRuntimeSnapshot> RollbackAsync(string connectorId, CancellationToken cancellationToken = default);
    IReadOnlyList<ConnectorRuntimeSnapshot> List();
}

public sealed class ConnectorRuntimeService : IConnectorRuntimeService
{
    private readonly IConnectorPackageService _packages;
    private readonly IConnectorRegistry _registry;
    private readonly IConnectorHostClient _host;
    private readonly Dictionary<string, ConnectorRuntimeSnapshot> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public ConnectorRuntimeService(IConnectorPackageService packages, IConnectorRegistry registry, IConnectorHostClient host) { _packages = packages; _registry = registry; _host = host; }

    public async Task<ConnectorRuntimeSnapshot> ActivateAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var package = await _packages.GetAsync(packageId, cancellationToken) ?? throw new KeyNotFoundException("Connector package was not found.");
        if (package.Status != ConnectorPackageStatus.Installed || !package.ExplicitlyApproved) throw new InvalidOperationException("Only explicitly approved installed packages can be activated.");
        if (package.Manifest.ExecutionPolicy is null) throw new InvalidOperationException("Connector execution policy is missing.");
        var compiledAssembly = Directory.EnumerateFiles(package.PackagePath, "*.dll", SearchOption.AllDirectories).FirstOrDefault(path => Path.GetFileName(path).Contains("Connector.Validation", StringComparison.OrdinalIgnoreCase));
        if (compiledAssembly == null) throw new InvalidOperationException("Compiled connector artifact was not found.");

        // Register only a manifest proxy. Generated assemblies are never loaded into
        // the Gram process; a separate connector host must execute provider code.
        _registry.Register(new IsolatedConnectorProxy(package, _host));
        var snapshot = new ConnectorRuntimeSnapshot(package.Manifest.Id, package.Id, package.Version, ConnectorRuntimeStatus.Healthy, DateTimeOffset.UtcNow);
        lock (_sync) _active[package.Manifest.Id] = snapshot;
        return snapshot;
    }

    public Task<ConnectorRuntimeSnapshot> HealthAsync(string connectorId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_active.TryGetValue(connectorId, out var state)) return Task.FromResult(state);
        }
        return Task.FromResult(new ConnectorRuntimeSnapshot(connectorId, "", "", ConnectorRuntimeStatus.Inactive, DateTimeOffset.UtcNow));
    }

    public Task<bool> DisableAsync(string connectorId, CancellationToken cancellationToken = default)
    {
        var removed = _registry.Unregister(connectorId);
        lock (_sync)
        {
            if (_active.TryGetValue(connectorId, out var state)) _active[connectorId] = state with { Status = ConnectorRuntimeStatus.Disabled, UpdatedAt = DateTimeOffset.UtcNow };
        }
        return Task.FromResult(removed);
    }

    public async Task<ConnectorRuntimeSnapshot> RollbackAsync(string connectorId, CancellationToken cancellationToken = default)
    {
        var candidates = (await _packages.ListAsync(cancellationToken)).Where(x => x.Manifest.Id.Equals(connectorId, StringComparison.OrdinalIgnoreCase) && x.Status == ConnectorPackageStatus.Installed && x.ExplicitlyApproved).OrderByDescending(x => x.UpdatedAt).ToList();
        if (candidates.Count < 2) throw new InvalidOperationException("No previous installed connector version is available.");
        await DisableAsync(connectorId, cancellationToken);
        return await ActivateAsync(candidates[1].Id, cancellationToken);
    }

    public IReadOnlyList<ConnectorRuntimeSnapshot> List() { lock (_sync) return _active.Values.OrderBy(x => x.ConnectorId).ToList(); }

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
