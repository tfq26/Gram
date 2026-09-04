using System.Text.Json;

namespace Gram.Blazor.Shared;

public enum ConnectorTransport { Native, Mcp, LocalAgent }
public enum ActionRiskLevel { Safe, Moderate, Sensitive }
public enum ApprovalScope { Once, Run, Session }
public enum ApprovalDecision { Approve, Deny }

public sealed record ConnectorDescriptor(string Id, string Name, ConnectorTransport Transport, string Description, bool RequiresConnection, bool IsEnabled = true);
public sealed record ConnectorConnection(string Id, string ConnectorId, string DisplayName, IReadOnlyList<string> Scopes, bool IsConnected, DateTimeOffset? ExpiresAt = null, string UserId = "default", string AuthenticationType = "oauth", DateTimeOffset? LastVerifiedAt = null, string? ClientId = null);
public sealed record ConnectorAction(string Id, string ConnectorId, string Name, string Description, ActionRiskLevel RiskLevel, JsonElement? ParametersSchema = null, bool RequiresApproval = false, IReadOnlyList<string>? RequiredScopes = null);
public sealed record ConnectorConnectionRegistrationRequest(string ConnectorId, string DisplayName, IReadOnlyList<string> Scopes, string AuthenticationType = "oauth", DateTimeOffset? ExpiresAt = null, string? ClientId = null);
public sealed record ConnectorActionRequest(string ConnectorId, string ActionId, JsonElement Parameters, string? ConnectionId = null, string? RunId = null, string? IdempotencyKey = null, string? UserId = null);
public sealed record ConnectorActionResult(bool Success, string Status, string? Message = null, JsonElement? Data = null, bool RequiresApproval = false, string? ApprovalId = null);
public sealed record ActionApproval(string Id, string UserId, string ConnectorId, string ActionId, ApprovalDecision Decision, ApprovalScope Scope, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string? RunId = null);
public sealed record RecordActionApprovalRequest(ConnectorActionRequest Request, ApprovalDecision Decision, ApprovalScope Scope);
public sealed record ActionAuditEvent(string Id, string UserId, string ConnectorId, string ActionId, ActionRiskLevel RiskLevel, string Status, DateTimeOffset CreatedAt, string? RunId = null, string? Error = null);
public sealed record WebhookEvent(string Id, string ConnectorId, string EventType, string UserId, JsonElement Payload, DateTimeOffset ReceivedAt, string IdempotencyKey);
public sealed record ConnectorHostExecuteRequest(string PackageId, ConnectorActionRequest Action);

public interface IConnectedConnector
{
    ConnectorDescriptor Descriptor { get; }
    IReadOnlyList<ConnectorAction> Actions { get; }
    Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default);
}

public interface IConnectorRegistry
{
    IReadOnlyList<ConnectorDescriptor> ListConnectors();
    IReadOnlyList<ConnectorAction> ListActions(string? connectorId = null);
    IConnectedConnector? Get(string connectorId);
    void Register(IConnectedConnector connector);
    bool Unregister(string connectorId);
}

public interface IActionPermissionService
{
    ActionRiskLevel Classify(ConnectorAction action);
    Task<ActionApproval?> GetApprovalAsync(string userId, ConnectorActionRequest request, CancellationToken cancellationToken = default);
    Task<ActionApproval> RecordApprovalAsync(string userId, ConnectorActionRequest request, ApprovalDecision decision, ApprovalScope scope, CancellationToken cancellationToken = default);
}
