using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public interface ISyncStatusService
{
    SyncStatus GetStatus();
    bool IsRunning();
    bool IsSyncEnabled();
    void SetSyncEnabled(bool enabled);
    void StartSync();
    void EndSync();
    void SetNextScheduledSync(DateTime nextSync);
    void UpdateMailboxStatus(string mailbox, int itemsSynced, int errors, string status);
    void UpdateMailboxStatus(string mailbox, int evaluated, int created, int updated, int deleted, int unchanged, int errors, string status);
    Task<bool> TryAcquireSyncLock();
    void ReleaseSyncLock();
}
