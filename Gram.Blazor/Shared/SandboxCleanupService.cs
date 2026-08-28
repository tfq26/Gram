using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed class SandboxCleanupService : BackgroundService
{
    private readonly IGramBoxService _gramBox;
    private readonly ILogger<SandboxCleanupService> _logger;

    public SandboxCleanupService(IGramBoxService gramBox, ILogger<SandboxCleanupService> logger)
    {
        _gramBox = gramBox;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var session in _gramBox.ActiveSessions.Where(session => session.Status is SandboxStatus.Ready or SandboxStatus.Error && DateTime.UtcNow - session.LastActiveAt > TimeSpan.FromMinutes(30)).ToList())
            {
                try
                {
                    await _gramBox.CloseSandboxAsync(session.Id, force: true, stoppingToken);
                    _logger.LogInformation("Cleaned up inactive sandbox {SandboxId}.", session.Id);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not clean up inactive sandbox {SandboxId}.", session.Id); }
            }
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
}
