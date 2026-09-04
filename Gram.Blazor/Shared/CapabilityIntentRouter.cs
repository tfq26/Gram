namespace Gram.Blazor.Shared;

public enum CapabilityResolutionKind { ExistingConnector, GenericCapability, McpConnector, GenerateConnector, Clarify }
public sealed record CapabilityResolution(CapabilityResolutionKind Kind, string Capability, string Reason, string? ConnectorId = null, string? ActionId = null, ActionRiskLevel RiskLevel = ActionRiskLevel.Safe, bool RequiresApproval = false);

public interface ICapabilityIntentRouter
{
    CapabilityResolution Resolve(string request);
}

/// Resolves user intent before tool execution. It is deliberately deterministic
/// today; an LLM may later propose candidates, but this policy remains authoritative.
public sealed class CapabilityIntentRouter : ICapabilityIntentRouter
{
    private readonly IConnectorRegistry _registry;
    public CapabilityIntentRouter(IConnectorRegistry registry) => _registry = registry;

    public CapabilityResolution Resolve(string request)
    {
        var text = request?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return new(CapabilityResolutionKind.Clarify, "none", "The request is empty.");

        var actions = _registry.ListActions();
        var mentioned = _registry.ListConnectors().FirstOrDefault(c => text.Contains(c.Name, StringComparison.OrdinalIgnoreCase) || text.Contains(c.Id, StringComparison.OrdinalIgnoreCase));
        if (mentioned != null)
        {
            if (!mentioned.IsEnabled) return new(CapabilityResolutionKind.Clarify, mentioned.Name, "The matching capability exists but is disabled for this release.", mentioned.Id);
            var action = actions.FirstOrDefault(a => a.ConnectorId == mentioned.Id && (text.Contains(a.Name, StringComparison.OrdinalIgnoreCase) || text.Contains(a.Id, StringComparison.OrdinalIgnoreCase)));
            if (action != null) return new(CapabilityResolutionKind.ExistingConnector, $"{mentioned.Name} · {action.Name}", "An installed connector exposes the requested operation.", mentioned.Id, action.Id, action.RiskLevel, action.RequiresApproval || action.RiskLevel != ActionRiskLevel.Safe);
            return new(CapabilityResolutionKind.GenerateConnector, mentioned.Name, "The service was identified, but no installed connector action can satisfy the request.", mentioned.Id);
        }

        if (LooksLikeHttpRequest(text))
            return new(CapabilityResolutionKind.GenericCapability, "controlled-http", "The request can be satisfied by a generic HTTP action; no application connector is needed.", RiskLevel: ActionRiskLevel.Sensitive, RequiresApproval: true);
        if (ContainsAny(text, "remember", "memory", "what do you know about"))
            return new(CapabilityResolutionKind.GenericCapability, "memory", "Use the structured memory capability.");
        if (ContainsAny(text, "remind me", "reminder", "schedule"))
            return new(CapabilityResolutionKind.ExistingConnector, "reminders", "Use the built-in reminders capability.", "reminders", "create", ActionRiskLevel.Moderate, true);
        if (ContainsAny(text, "browse", "browser", "website", "web page"))
            return new(CapabilityResolutionKind.GenericCapability, "browser", "Use the approved browser capability; active-browser control requires the desktop agent.");
        if (ContainsAny(text, "research", "look up", "find online", "latest"))
            return new(CapabilityResolutionKind.GenericCapability, "web-search", "Use web search and source validation.");
        if (ContainsAny(text, "connector", "connect to", "integrate"))
            return new(CapabilityResolutionKind.GenerateConnector, "new-connector", "No matching installed connector was found; connector generation can be proposed after clarification.");
        return new(CapabilityResolutionKind.Clarify, "general", "No safe capability match was found.");
    }

    private static bool LooksLikeHttpRequest(string text) => ContainsAny(text, "post to", "http post", "get from", "call endpoint", "api endpoint");
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
}
