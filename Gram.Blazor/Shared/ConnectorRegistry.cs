namespace Gram.Blazor.Shared;

public sealed class ConnectorRegistry : IConnectorRegistry
{
    private readonly Dictionary<string, IConnectedConnector> _connectors;
    private readonly object _sync = new();

    public ConnectorRegistry(IEnumerable<IConnectedConnector> connectors) => _connectors = connectors.ToDictionary(x => x.Descriptor.Id, StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<ConnectorDescriptor> ListConnectors() { lock (_sync) return _connectors.Values.Select(x => x.Descriptor).ToList(); }
    public IReadOnlyList<ConnectorAction> ListActions(string? connectorId = null) { lock (_sync) return _connectors.Values.Where(x => connectorId == null || x.Descriptor.Id.Equals(connectorId, StringComparison.OrdinalIgnoreCase)).SelectMany(x => x.Actions).ToList(); }
    public IConnectedConnector? Get(string connectorId) { lock (_sync) return _connectors.GetValueOrDefault(connectorId); }
    public void Register(IConnectedConnector connector) { lock (_sync) _connectors[connector.Descriptor.Id] = connector; }
    public bool Unregister(string connectorId) { lock (_sync) return _connectors.Remove(connectorId); }
}

public sealed class ConnectorPermissionService : IActionPermissionService
{
    private readonly Dictionary<string, ActionApproval> _sessionApprovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public ActionRiskLevel Classify(ConnectorAction action) => action.RiskLevel;

    public Task<ActionApproval?> GetApprovalAsync(string userId, ConnectorActionRequest request, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var key = Key(userId, request);
            if (_sessionApprovals.TryGetValue(key, out var approval) && approval.ExpiresAt > DateTimeOffset.UtcNow && approval.Decision == ApprovalDecision.Approve)
                return Task.FromResult<ActionApproval?>(approval);
        }
        return Task.FromResult<ActionApproval?>(null);
    }

    public Task<ActionApproval> RecordApprovalAsync(string userId, ConnectorActionRequest request, ApprovalDecision decision, ApprovalScope scope, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var approval = new ActionApproval(Guid.NewGuid().ToString("N"), userId, request.ConnectorId, request.ActionId, decision, scope, now, now.Add(scope == ApprovalScope.Session ? TimeSpan.FromHours(12) : TimeSpan.FromMinutes(30)), request.RunId);
        if (decision == ApprovalDecision.Approve && scope == ApprovalScope.Session)
            lock (_sync) _sessionApprovals[Key(userId, request)] = approval;
        return Task.FromResult(approval);
    }

    private static string Key(string userId, ConnectorActionRequest request) => $"{userId}:{request.ConnectorId}:{request.ActionId}";
}
