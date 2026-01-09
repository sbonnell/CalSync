using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public class SyncBackgroundService : BackgroundService
{
    private readonly ILogger<SyncBackgroundService> _logger;
    private readonly ICalendarSyncService _syncService;
    private readonly ISyncStatusService _statusService;
    private readonly ExchangeOnPremiseSettings _onPremiseSettings;
    private readonly SyncSettings _syncSettings;

    public SyncBackgroundService(
        ILogger<SyncBackgroundService> logger,
        ICalendarSyncService syncService,
        ISyncStatusService statusService,
        ExchangeOnPremiseSettings onPremiseSettings,
        SyncSettings syncSettings)
    {
        _logger = logger;
        _syncService = syncService;
        _statusService = statusService;
        _onPremiseSettings = onPremiseSettings;
        _syncSettings = syncSettings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sync background service starting...");

        // Wait a bit for services to fully initialize
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Try to acquire the sync lock
                if (await _statusService.TryAcquireSyncLock())
                {
                    try
                    {
                        await _syncService.SyncAllMailboxesAsync(_onPremiseSettings.GetMailboxMappings());
                    }
                    finally
                    {
                        _statusService.ReleaseSyncLock();
                    }
                }
                else
                {
                    _logger.LogInformation("Skipping scheduled sync - manual sync is running");
                }

                var nextSync = DateTime.UtcNow.AddMinutes(_syncSettings.SyncIntervalMinutes);
                _statusService.SetNextScheduledSync(nextSync);

                _logger.LogInformation("Next sync in {Minutes} minutes", _syncSettings.SyncIntervalMinutes);
                await Task.Delay(TimeSpan.FromMinutes(_syncSettings.SyncIntervalMinutes), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during sync operation");
                _logger.LogInformation("Retrying in 1 minute...");

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Sync background service stopping...");
    }
}
