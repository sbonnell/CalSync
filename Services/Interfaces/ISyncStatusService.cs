using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public interface ISyncStatusService
{
    SyncStatus GetStatus();
    bool IsRunning();
    void StartSync();
    void EndSync();
    void SetNextScheduledSync(DateTime nextSync);
    void UpdateMailboxStatus(string mailbox, int itemsSynced, int errors, string status);
    Task<bool> TryAcquireSyncLock();
    void ReleaseSyncLock();
}
