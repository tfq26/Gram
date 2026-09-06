using System.Text.Json;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ConnectorActionExecutorTests
{
    [Fact]
    public async Task UnknownActionIsReportedAsNotFound()
    {
        var executor = CreateExecutor(new FakePermission(false));

        var result = await executor.ExecuteAsync("user-1", Request("missing"));

        Assert.False(result.Found);
        Assert.Equal("action_not_found", result.Result.Status);
    }

    [Fact]
    public async Task LegacyDefaultIdentityCannotExecuteConnectorActions()
    {
        var connector = new FakeConnector(new ConnectorAction("read", "fake", "Read", "", ActionRiskLevel.Safe));
        var executor = CreateExecutor(new FakePermission(false), connector);

        var result = await executor.ExecuteAsync("default", Request("read"));

        Assert.True(result.Found);
        Assert.Equal("unauthorized", result.Result.Status);
        Assert.Equal(0, connector.ExecutionCount);
    }

    [Fact]
    public async Task ApprovalRequiredActionDoesNotExecuteWithoutApproval()
    {
        var connector = new FakeConnector(new ConnectorAction("write", "fake", "Write", "", ActionRiskLevel.Moderate));
        var executor = CreateExecutor(new FakePermission(false), connector);

        var result = await executor.ExecuteAsync("user-1", Request("write"));

        Assert.True(result.Found);
        Assert.Equal("approval_required", result.Result.Status);
        Assert.Equal(0, connector.ExecutionCount);
    }

    [Fact]
    public async Task ApprovedActionExecutesWithResolvedConnectionAndUser()
    {
        var connector = new FakeConnector(new ConnectorAction("write", "fake", "Write", "", ActionRiskLevel.Moderate));
        var executor = CreateExecutor(new FakePermission(true), connector);

        var result = await executor.ExecuteAsync("user-1", Request("write"));

        Assert.True(result.Result.Success);
        Assert.Equal(1, connector.ExecutionCount);
        Assert.Equal("user-1", connector.LastRequest!.UserId);
        Assert.Equal("connection-1", connector.LastRequest.ConnectionId);
    }

    private static ConnectorActionExecutor CreateExecutor(FakePermission permission, FakeConnector? connector = null)
    {
        connector ??= new FakeConnector(new ConnectorAction("read", "fake", "Read", "", ActionRiskLevel.Safe));
        return new ConnectorActionExecutor(
            new FakeRegistry(connector),
            permission,
            new FakeResolver(),
            []);
    }

    private static ConnectorActionRequest Request(string actionId) => new("fake", actionId, JsonSerializer.SerializeToElement(new { value = "test" }));

    private sealed class FakeConnector(ConnectorAction action) : IConnectedConnector
    {
        public ConnectorDescriptor Descriptor { get; } = new("fake", "Fake", ConnectorTransport.Native, "", true);
        public IReadOnlyList<ConnectorAction> Actions { get; } = [action];
        public int ExecutionCount { get; private set; }
        public ConnectorActionRequest? LastRequest { get; private set; }
        public Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            LastRequest = request;
            return Task.FromResult(new ConnectorActionResult(true, "executed"));
        }
    }

    private sealed class FakeRegistry(FakeConnector connector) : IConnectorRegistry
    {
        public IReadOnlyList<ConnectorDescriptor> ListConnectors() => [connector.Descriptor];
        public IReadOnlyList<ConnectorAction> ListActions(string? connectorId = null) => connector.Actions;
        public IConnectedConnector? Get(string connectorId) => connectorId.Equals(connector.Descriptor.Id, StringComparison.OrdinalIgnoreCase) ? connector : null;
        public void Register(IConnectedConnector value) { }
        public bool Unregister(string connectorId) => false;
    }

    private sealed class FakeResolver : IConnectorConnectionResolver
    {
        public Task<ConnectionResolution> ResolveAsync(string userId, ConnectorDescriptor connector, ConnectorAction action, string? requestedConnectionId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionResolution(ConnectionResolutionStatus.Ready, "ready", new ConnectorConnection("connection-1", connector.Id, "Fake account", [], true, DateTimeOffset.UtcNow.AddHours(1), userId)));
    }

    private sealed class FakePermission(bool approved) : IActionPermissionService
    {
        public ActionRiskLevel Classify(ConnectorAction action) => action.RiskLevel;
        public bool RequiresApproval(ConnectorAction action) => action.RiskLevel != ActionRiskLevel.Safe;
        public Task<ActionApproval?> GetApprovalAsync(string userId, ConnectorActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult<ActionApproval?>(approved ? new ActionApproval("approval-1", userId, request.ConnectorId, request.ActionId, ApprovalDecision.Approve, ApprovalScope.Once, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1)) : null);
        public Task<ActionApproval?> TryConsumeApprovalAsync(string userId, ConnectorActionRequest request, CancellationToken cancellationToken = default) => GetApprovalAsync(userId, request, cancellationToken);
        public Task<ActionApproval?> PeekApprovalAsync(string userId, ConnectorActionRequest request, CancellationToken cancellationToken = default) => GetApprovalAsync(userId, request, cancellationToken);
        public Task<ActionApproval> RecordApprovalAsync(string userId, ConnectorActionRequest request, ApprovalDecision decision, ApprovalScope scope, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> RevokeApprovalAsync(string userId, ConnectorActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
