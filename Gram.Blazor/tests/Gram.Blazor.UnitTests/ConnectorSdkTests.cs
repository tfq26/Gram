using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class ConnectorSdkTests
{
    [Fact]
    public async Task GeneratesSafePreviewWithApprovalMetadata()
    {
        var sdk = new ConnectorSdk();
        var preview = await sdk.GeneratePreviewAsync(new ConnectorGenerationRequest("Example Music", "Control a music service", Actions: [
            new ConnectorManifestAction("play", "Play", "Start playback.", ActionRiskLevel.Moderate, [], true)]));

        Assert.Equal("example-music", preview.Manifest.Id);
        Assert.Contains(preview.Manifest.Actions, action => action.Id == "play" && action.RequiresApproval);
        Assert.False(preview.CanInstall);
        Assert.Contains(preview.Warnings, warning => warning.Contains("scaffold", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("IConnectedConnector", preview.AdapterSource);
    }

    [Fact]
    public void RejectsForbiddenGeneratedOperationsAndUnapprovedSensitiveActions()
    {
        var sdk = new ConnectorSdk();
        var warnings = sdk.Validate(
            new ConnectorManifest("unsafe", "Unsafe", "0.1.0", ConnectorTransport.Native, "", [], [
                new ConnectorManifestAction("delete", "Delete", "Delete data", ActionRiskLevel.Sensitive, [], false)]),
            "Process.Start(\"zsh -c\");");

        Assert.Contains(warnings, warning => warning.Contains("require approval", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, warning => warning.Contains("forbidden", StringComparison.OrdinalIgnoreCase));
    }
}
