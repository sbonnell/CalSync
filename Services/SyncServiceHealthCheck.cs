using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ExchangeCalendarSync.Services;

public class SyncServiceHealthCheck : IHealthCheck
{
    private readonly ISyncStatusService _statusService;

    public SyncServiceHealthCheck(ISyncStatusService statusService)
    {
        _statusService = statusService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = _statusService.GetStatus();

        var data = new Dictionary<string, object>
        {
            { "isRunning", status.IsRunning },
            { "lastSyncTime", status.LastSyncTime?.ToString("o") ?? "never" },
            { "nextScheduledSync", status.NextScheduledSync?.ToString("o") ?? "unknown" },
            { "totalItemsSynced", status.TotalItemsSynced },
            { "totalErrors", status.TotalErrors },
            { "mailboxCount", status.MailboxStatuses.Count }
        };

        // Consider unhealthy if there have been errors in the last sync
        // or if no sync has occurred yet after a reasonable time
        if (status.TotalErrors > 0 && status.LastSyncTime.HasValue)
        {
            var errorRate = (double)status.TotalErrors / Math.Max(1, status.TotalItemsSynced + status.TotalErrors);
            if (errorRate > 0.5) // More than 50% errors
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "High error rate in sync operations",
                    data: data));
            }
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "Sync service is operational",
            data: data));
    }
}
