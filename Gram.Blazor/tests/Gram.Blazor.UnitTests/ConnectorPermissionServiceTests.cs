using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class ConnectorPermissionServiceTests
{
    private readonly ConnectorPermissionService _service = new();

    [Fact]
    public async Task ApprovalScopeOnce_ConsumedOnFirstGet_ReplayReturnsNull()
    {
        var request = CreateRequest(connectorId: "github", actionId: "create_issue", json: "{\"title\":\"Bug\",\"body\":\"Broken\"}");

        // Record Once approval
        var recorded = await _service.RecordApprovalAsync("user1", request, ApprovalDecision.Approve, ApprovalScope.Once);
        Assert.Equal(ApprovalDecision.Approve, recorded.Decision);
        Assert.Equal(ApprovalScope.Once, recorded.Scope);

        // First retrieval consumes it
        var first = await _service.GetApprovalAsync("user1", request);
        Assert.NotNull(first);
        Assert.Equal(recorded.Id, first.Id);

        // Replay attempt fails (already consumed)
        var second = await _service.GetApprovalAsync("user1", request);
        Assert.Null(second);
    }

    [Fact]
    public async Task ApprovalScopeRun_ValidAcrossCallsWithinRun_InvalidForDifferentRun()
    {
        var requestRunA = CreateRequest(connectorId: "postgres", actionId: "execute_query", json: "{\"query\":\"DROP TABLE temp;\"}", runId: "run-alpha");

        await _service.RecordApprovalAsync("user1", requestRunA, ApprovalDecision.Approve, ApprovalScope.Run);

        // Same run can call multiple times
        var first = await _service.GetApprovalAsync("user1", requestRunA);
        Assert.NotNull(first);
        var second = await _service.GetApprovalAsync("user1", requestRunA);
        Assert.NotNull(second);

        // Different run cannot reuse
        var requestRunB = requestRunA with { RunId = "run-beta" };
        var runBResult = await _service.GetApprovalAsync("user1", requestRunB);
        Assert.Null(runBResult);

        // Request with missing runId cannot reuse
        var requestNoRun = requestRunA with { RunId = null };
        var noRunResult = await _service.GetApprovalAsync("user1", requestNoRun);
        Assert.Null(noRunResult);
    }

    [Fact]
    public async Task ApprovalScopeSession_ValidAcrossMultipleCallsAndRuns()
    {
        var request = CreateRequest(connectorId: "aws", actionId: "list_buckets", json: "{}");

        await _service.RecordApprovalAsync("user1", request, ApprovalDecision.Approve, ApprovalScope.Session);

        var first = await _service.GetApprovalAsync("user1", request with { RunId = "run-1" });
        Assert.NotNull(first);

        var second = await _service.GetApprovalAsync("user1", request with { RunId = "run-2" });
        Assert.NotNull(second);
    }

    [Fact]
    public async Task ParameterCanonicalization_IgnoresKeyOrdering_DistinguishesDifferentValues()
    {
        // Two JSONs with different key order but same content
        var req1 = CreateRequest(connectorId: "api", actionId: "call", json: "{\"b\": 20, \"a\": 10}");
        var req2 = CreateRequest(connectorId: "api", actionId: "call", json: "{\"a\": 10, \"b\": 20}");

        // Different parameter value
        var reqDifferent = CreateRequest(connectorId: "api", actionId: "call", json: "{\"a\": 99, \"b\": 20}");

        await _service.RecordApprovalAsync("user1", req1, ApprovalDecision.Approve, ApprovalScope.Session);

        // req2 should match because canonical hash sorts keys
        var match = await _service.GetApprovalAsync("user1", req2);
        Assert.NotNull(match);

        // reqDifferent must not match
        var mismatch = await _service.GetApprovalAsync("user1", reqDifferent);
        Assert.Null(mismatch);
    }

    [Fact]
    public async Task Denial_InvalidatesExistingSessionAndRunApprovals()
    {
        var request = CreateRequest(connectorId: "deploy", actionId: "deploy_prod", json: "{\"env\":\"prod\"}", runId: "run-9");

        // First approve
        await _service.RecordApprovalAsync("user1", request, ApprovalDecision.Approve, ApprovalScope.Session);
        var beforeDeny = await _service.PeekApprovalAsync("user1", request);
        Assert.NotNull(beforeDeny);

        // Now explicitly deny
        var denied = await _service.RecordApprovalAsync("user1", request, ApprovalDecision.Deny, ApprovalScope.Session);
        Assert.Equal(ApprovalDecision.Deny, denied.Decision);

        // Check should now return null because denial purged prior approvals and blocks
        var afterDeny = await _service.GetApprovalAsync("user1", request);
        Assert.Null(afterDeny);
    }

    [Fact]
    public async Task TenantAndUserIsolation_EnforcesStrictSeparation()
    {
        var request = CreateRequest(connectorId: "stripe", actionId: "refund", json: "{\"chargeId\":\"ch_123\"}");

        await _service.RecordApprovalAsync("tenant-A:user-1", request, ApprovalDecision.Approve, ApprovalScope.Session);

        // Tenant B / User 2 should NOT have approval
        var crossTenant = await _service.GetApprovalAsync("tenant-B:user-1", request);
        Assert.Null(crossTenant);

        var crossUser = await _service.GetApprovalAsync("tenant-A:user-2", request);
        Assert.Null(crossUser);

        // Legitimate user matches
        var valid = await _service.GetApprovalAsync("tenant-A:user-1", request);
        Assert.NotNull(valid);
    }

    [Fact]
    public async Task ConnectionBinding_EnforcesSpecificConnectionId()
    {
        var reqConn1 = CreateRequest(connectorId: "slack", actionId: "post", json: "{\"channel\":\"general\"}", connectionId: "conn-workspace-1");
        var reqConn2 = reqConn1 with { ConnectionId = "conn-workspace-2" };

        await _service.RecordApprovalAsync("user1", reqConn1, ApprovalDecision.Approve, ApprovalScope.Session);

        Assert.NotNull(await _service.GetApprovalAsync("user1", reqConn1));
        Assert.Null(await _service.GetApprovalAsync("user1", reqConn2));
    }

    [Fact]
    public void RequiresApproval_ConsidersRiskLevelAndExplicitFlag()
    {
        var safeWithoutFlag = new ConnectorAction("read", "test", "Read", "Desc", ActionRiskLevel.Safe, RequiresApproval: false);
        var safeWithFlag = new ConnectorAction("read_sensitive", "test", "Read Sensitive", "Desc", ActionRiskLevel.Safe, RequiresApproval: true);
        var moderate = new ConnectorAction("write", "test", "Write", "Desc", ActionRiskLevel.Moderate, RequiresApproval: false);
        var sensitive = new ConnectorAction("delete", "test", "Delete", "Desc", ActionRiskLevel.Sensitive, RequiresApproval: false);

        Assert.False(_service.RequiresApproval(safeWithoutFlag));
        Assert.True(_service.RequiresApproval(safeWithFlag));
        Assert.True(_service.RequiresApproval(moderate));
        Assert.True(_service.RequiresApproval(sensitive));
    }

    [Fact]
    public async Task RevokeApprovalAsync_ClearsStoredApprovals()
    {
        var request = CreateRequest(connectorId: "ssh", actionId: "reboot", json: "{}");

        await _service.RecordApprovalAsync("user1", request, ApprovalDecision.Approve, ApprovalScope.Session);
        Assert.NotNull(await _service.PeekApprovalAsync("user1", request));

        var revoked = await _service.RevokeApprovalAsync("user1", request);
        Assert.True(revoked);

        Assert.Null(await _service.PeekApprovalAsync("user1", request));
    }

    [Fact]
    public async Task SessionApproval_DoesNotSurviveServiceRestart_WhileOnceDoes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gram-approval-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["GRAM_DATA_DIR"] = directory })
                .Build();
            var storage = new StorageService(configuration);
            var sessionRequest = CreateRequest(connectorId: "calendar", actionId: "create_event", json: "{\"title\":\"Session Review\"}");
            var onceRequest = CreateRequest(connectorId: "calendar", actionId: "delete_event", json: "{\"eventId\":\"evt-1\"}");

            var first = new ConnectorPermissionService(storage);
            await first.RecordApprovalAsync("user1", sessionRequest, ApprovalDecision.Approve, ApprovalScope.Session);
            await first.RecordApprovalAsync("user1", onceRequest, ApprovalDecision.Approve, ApprovalScope.Once);

            var restarted = new ConnectorPermissionService(storage);

            // Session approval must NOT survive service restart (in-memory only)
            var restoredSession = await restarted.GetApprovalAsync("user1", sessionRequest);
            Assert.Null(restoredSession);

            // Once approval DOES survive service restart in persistent storage and is consumable once
            var restoredOnce = await restarted.GetApprovalAsync("user1", onceRequest);
            Assert.NotNull(restoredOnce);
            Assert.Equal(ApprovalScope.Once, restoredOnce.Scope);

            // Replay after consumption fails
            var replayedOnce = await restarted.GetApprovalAsync("user1", onceRequest);
            Assert.Null(replayedOnce);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                try { Directory.Delete(directory, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task OnceApproval_ConcurrentCallers_SingleProcess_OnlyOneSucceeds()
    {
        var request = CreateRequest(connectorId: "payments", actionId: "charge", json: "{\"amount\":100}");
        await _service.RecordApprovalAsync("user1", request, ApprovalDecision.Approve, ApprovalScope.Once);

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(async () => await _service.GetApprovalAsync("user1", request)))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(r => r != null);
        var failureCount = results.Count(r => r == null);

        Assert.Equal(1, successCount);
        Assert.Equal(19, failureCount);
    }

    [Fact]
    public async Task OnceApproval_ConcurrentInstances_SharedStorage_OnlyOneSucceeds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gram-concurrency-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["GRAM_DATA_DIR"] = directory })
                .Build();
            var storage = new StorageService(configuration);
            var request = CreateRequest(connectorId: "crypto", actionId: "transfer", json: "{\"to\":\"wallet_abc\",\"amount\":50}");

            // Create primary instance and record 1 Once approval
            var primary = new ConnectorPermissionService(storage);
            await primary.RecordApprovalAsync("user1", request, ApprovalDecision.Approve, ApprovalScope.Once);

            // Create 10 distinct service instances sharing the same storage
            var instances = Enumerable.Range(0, 10)
                .Select(_ => new ConnectorPermissionService(storage))
                .ToList();

            // Barrier to ensure all instances start consumption at the exact same moment
            var barrier = new Barrier(10);
            var tasks = instances.Select(inst => Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await inst.GetApprovalAsync("user1", request);
            })).ToArray();

            var results = await Task.WhenAll(tasks);
            var successCount = results.Count(r => r != null);
            var failureCount = results.Count(r => r == null);

            Assert.Equal(1, successCount);
            Assert.Equal(9, failureCount);

            // All subsequent calls across all instances must return null
            foreach (var inst in instances)
            {
                Assert.Null(await inst.PeekApprovalAsync("user1", request));
                Assert.Null(await inst.GetApprovalAsync("user1", request));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                try { Directory.Delete(directory, recursive: true); } catch { }
            }
        }
    }

    private static ConnectorActionRequest CreateRequest(
        string connectorId,
        string actionId,
        string json,
        string? connectionId = "conn-default",
        string? runId = null)
    {
        using var doc = JsonDocument.Parse(json);
        return new ConnectorActionRequest(
            ConnectorId: connectorId,
            ActionId: actionId,
            Parameters: doc.RootElement.Clone(),
            ConnectionId: connectionId,
            RunId: runId
        );
    }
}
