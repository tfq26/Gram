using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed class GramCircuitHandler : CircuitHandler
{
    private readonly ILogger<GramCircuitHandler> _logger;

    public GramCircuitHandler(ILogger<GramCircuitHandler> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogWarning("[GramCircuitHandler] Real-time interactive session disconnected for circuit {CircuitId}", circuit.Id);
        return base.OnConnectionDownAsync(circuit, cancellationToken);
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[GramCircuitHandler] Circuit closed: {CircuitId}", circuit.Id);
        return base.OnCircuitClosedAsync(circuit, cancellationToken);
    }
}
