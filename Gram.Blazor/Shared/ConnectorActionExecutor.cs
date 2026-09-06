namespace Gram.Blazor.Shared;

public interface IConnectorConnectionRefresher
{
    string ConnectorId { get; }
    Task<bool> TryRefreshAsync(string userId, string connectionId, CancellationToken cancellationToken = default);
}

public sealed record ConnectorActionExecution(bool Found, ConnectorActionResult Result);

public sealed class ConnectorActionExecutor
{
    private readonly IConnectorRegistry _registry;
    private readonly IActionPermissionService _permissions;
    private readonly IConnectorConnectionResolver _connections;
    private readonly IReadOnlyDictionary<string, IConnectorConnectionRefresher> _refreshers;

    public ConnectorActionExecutor(
        IConnectorRegistry registry,
        IActionPermissionService permissions,
        IConnectorConnectionResolver connections,
        IEnumerable<IConnectorConnectionRefresher> refreshers)
    {
        _registry = registry;
        _permissions = permissions;
        _connections = connections;
        _refreshers = refreshers.ToDictionary(x => x.ConnectorId, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ConnectorActionExecution> ExecuteAsync(string userId, ConnectorActionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Equals("default", StringComparison.OrdinalIgnoreCase))
            return new(true, new ConnectorActionResult(false, "unauthorized", "An authenticated connector owner is required."));

        var connector = _registry.Get(request.ConnectorId);
        var action = _registry.ListActions(request.ConnectorId)
            .FirstOrDefault(x => x.Id.Equals(request.ActionId, StringComparison.OrdinalIgnoreCase));
        if (connector is null || action is null || !connector.Descriptor.IsEnabled)
            return NotFound("Connector action was not found or disabled.");

        var boundRequest = request with { UserId = userId };
        var resolution = await _connections.ResolveAsync(userId, connector.Descriptor, action, boundRequest.ConnectionId, cancellationToken);
        if (resolution.Status == ConnectionResolutionStatus.ReauthorizationRequired && resolution.Connection is not null &&
            _refreshers.TryGetValue(connector.Descriptor.Id, out var refresher))
        {
            try
            {
                if (await refresher.TryRefreshAsync(userId, resolution.Connection.Id, cancellationToken))
                    resolution = await _connections.ResolveAsync(userId, connector.Descriptor, action, resolution.Connection.Id, cancellationToken);
            }
            catch
            {
                // Preserve the original reauthorization result for a safe, actionable response.
            }
        }

        if (resolution.Status != ConnectionResolutionStatus.Ready)
            return Success(new ConnectorActionResult(false, resolution.Status.ToString(), resolution.Message));

        boundRequest = boundRequest with { ConnectionId = resolution.Connection?.Id ?? boundRequest.ConnectionId };
        if (_permissions.RequiresApproval(action) && await _permissions.GetApprovalAsync(userId, boundRequest, cancellationToken) is null)
            return Success(new ConnectorActionResult(false, "approval_required", "This action requires user approval.", RequiresApproval: true));

        return Success(await connector.ExecuteAsync(boundRequest, cancellationToken));
    }

    private static ConnectorActionExecution Success(ConnectorActionResult result) => new(true, result);
    private static ConnectorActionExecution NotFound(string message) => new(false, new ConnectorActionResult(false, "action_not_found", message));
}
