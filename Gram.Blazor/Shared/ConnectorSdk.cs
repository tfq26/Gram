using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public sealed record ConnectorManifest(
    string Id,
    string Name,
    string Version,
    ConnectorTransport Transport,
    string Description,
    IReadOnlyList<string> RequiredScopes,
    IReadOnlyList<ConnectorManifestAction> Actions,
    IReadOnlyList<string> WebhookEvents = default!,
    ConnectorExecutionPolicy? ExecutionPolicy = null,
    IReadOnlyList<string>? CredentialNames = null,
    IReadOnlyList<ConnectorCredentialBinding>? CredentialBindings = null);

public sealed record ConnectorManifestAction(
    string Id,
    string Name,
    string Description,
    ActionRiskLevel RiskLevel,
    IReadOnlyList<string> RequiredScopes,
    bool RequiresApproval);

public sealed record ConnectorGenerationRequest(
    string Name,
    string Description,
    ConnectorTransport Transport = ConnectorTransport.Mcp,
    IReadOnlyList<ConnectorManifestAction>? Actions = null,
    IReadOnlyList<string>? RequiredScopes = null,
    string? ResearchQuestion = null,
    IReadOnlyList<string>? PreferredDomains = null);

public sealed record ConnectorGenerationPreview(
    ConnectorManifest Manifest,
    string AdapterSource,
    IReadOnlyList<string> Warnings,
    bool CanInstall,
    ResearchEvidencePacket? Research = null);

public interface IConnectorSdk
{
    Task<ConnectorGenerationPreview> GeneratePreviewAsync(ConnectorGenerationRequest request, CancellationToken cancellationToken = default);
    IReadOnlyList<string> Validate(ConnectorManifest manifest, string adapterSource);
}

public sealed class ConnectorSdk : IConnectorSdk
{
    private readonly ResearchEvidenceService? _research;
    private static readonly string[] ForbiddenSourcePatterns = ["Process.Start", "bash -c", "zsh -c", "powershell -Command", "File.Delete", "Directory.Delete", "eval(", "chmod 777"];

    public ConnectorSdk(ResearchEvidenceService? research = null) => _research = research;

    public async Task<ConnectorGenerationPreview> GeneratePreviewAsync(ConnectorGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Connector name is required.");
        var id = NormalizeId(request.Name);
        var actions = request.Actions?.Count > 0 ? request.Actions :
        [new ConnectorManifestAction("status", "Get status", $"Read status from {request.Name}.", ActionRiskLevel.Safe, [], false)];
        var manifest = new ConnectorManifest(id, request.Name.Trim(), "0.1.0", request.Transport, request.Description.Trim(), request.RequiredScopes ?? [], actions,
            ExecutionPolicy: new ConnectorExecutionPolicy([], 60, 256, 1, 1_000_000), CredentialNames: []);
        var source = GenerateAdapterSource(manifest);
        var warnings = Validate(manifest, source).ToList();
        ResearchEvidencePacket? research = null;
        if (_research != null && !string.IsNullOrWhiteSpace(request.ResearchQuestion))
        {
            research = await _research.BuildAsync(new ResearchRequest(request.ResearchQuestion, 8, request.PreferredDomains, RequirePrimarySources: true), cancellationToken);
            if (!research.IsReady) warnings.Add("Connector research is incomplete; resolve evidence gaps before installation.");
        }
        return new ConnectorGenerationPreview(manifest, source, warnings, warnings.Count == 0, research);
    }

    public IReadOnlyList<string> Validate(ConnectorManifest manifest, string adapterSource)
    {
        var warnings = new List<string>();
        if (!IsValidId(manifest.Id)) warnings.Add("Connector ID must contain only lowercase letters, numbers, and hyphens.");
        if (manifest.Actions.Count == 0) warnings.Add("A connector must declare at least one action.");
        if (manifest.Actions.Any(a => string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Name))) warnings.Add("Every action requires an ID and display name.");
        if (manifest.Actions.Any(a => a.RiskLevel != ActionRiskLevel.Safe && !a.RequiresApproval)) warnings.Add("Moderate and sensitive actions must require approval.");
        if (manifest.ExecutionPolicy is null) warnings.Add("A connector must declare an execution policy.");
        else
        {
            if (manifest.ExecutionPolicy.TimeoutSeconds is < 1 or > 300) warnings.Add("Connector timeout must be between 1 and 300 seconds.");
            if (manifest.ExecutionPolicy.MemoryLimitMb is < 64 or > 2048) warnings.Add("Connector memory limit must be between 64 and 2048 MB.");
            if (manifest.ExecutionPolicy.CpuCores is < 1 or > 4) warnings.Add("Connector CPU allocation must be between 1 and 4 cores.");
            if (manifest.ExecutionPolicy.AllowedDomains.Any(domain => domain.Contains('/') || domain.Contains(':') || domain == "*")) warnings.Add("Connector network policy contains an invalid or unrestricted domain.");
        }
        foreach (var pattern in ForbiddenSourcePatterns)
            if (adapterSource.Contains(pattern, StringComparison.OrdinalIgnoreCase)) warnings.Add($"Generated adapter contains forbidden operation: {pattern}");
        if (adapterSource.Contains("not_implemented", StringComparison.OrdinalIgnoreCase)) warnings.Add("Connector adapter is still a scaffold and must implement its declared actions before installation.");
        if (adapterSource.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) || adapterSource.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)) warnings.Add("Adapters must request credentials through the connection vault; secrets may not be embedded in source.");
        return warnings;
    }

    private static string GenerateAdapterSource(ConnectorManifest manifest)
    {
        var actions = string.Join(", ", manifest.Actions.Select(a => $"new ConnectorAction(\"{Escape(a.Id)}\", \"{manifest.Id}\", \"{Escape(a.Name)}\", \"{Escape(a.Description)}\", ActionRiskLevel.{a.RiskLevel}, RequiresApproval: {a.RequiresApproval.ToString().ToLowerInvariant()})"));
        return $"using Gram.Blazor.Shared;\n\n// Generated connector adapter preview. Review, compile, and sandbox-test before installation.\npublic sealed class {ToTypeName(manifest.Id)}Connector : IConnectedConnector\n{{\n    public ConnectorDescriptor Descriptor => new(\"{manifest.Id}\", \"{Escape(manifest.Name)}\", ConnectorTransport.{manifest.Transport}, \"{Escape(manifest.Description)}\", true);\n    public IReadOnlyList<ConnectorAction> Actions => [{actions}];\n    public Task<ConnectorActionResult> ExecuteAsync(ConnectorActionRequest request, CancellationToken cancellationToken = default)\n        => Task.FromResult(new ConnectorActionResult(false, \"not_implemented\", \"Generated adapter requires provider-specific implementation and review.\"));\n}}\n";
    }

    private static string NormalizeId(string value) => string.Join('-', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace("_", "-");
    private static bool IsValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-');
    private static string ToTypeName(string value) => string.Concat(value.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..])) ;
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
}
