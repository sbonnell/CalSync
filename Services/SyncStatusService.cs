using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public class SyncStatusService
{
    private readonly SyncStatus _status;
    private readonly SemaphoreSlim _syncLock;

    public SyncStatusService()
    {
        _status = new SyncStatus();
        _syncLock = new SemaphoreSlim(1, 1);
    }

    public SyncStatus GetStatus()
    {
        return _status;
    }

    public bool IsRunning()
    {
        return _status.IsRunning;
    }

    public void StartSync()
    {
        _status.IsRunning = true;
    }

    public void EndSync()
    {
        _status.IsRunning = false;
        _status.LastSyncTime = DateTime.UtcNow;
    }

    public void SetNextScheduledSync(DateTime nextSync)
    {
        _status.NextScheduledSync = nextSync;
    }

    public void UpdateMailboxStatus(string mailbox, int itemsSynced, int errors, string status)
    {
        if (!_status.MailboxStatuses.ContainsKey(mailbox))
        {
            _status.MailboxStatuses[mailbox] = new MailboxSyncStatus
            {
                MailboxEmail = mailbox
            };
        }

        var mailboxStatus = _status.MailboxStatuses[mailbox];
        mailboxStatus.LastSyncTime = DateTime.UtcNow;
        mailboxStatus.ItemsSynced += itemsSynced;
        mailboxStatus.Errors += errors;
        mailboxStatus.Status = status;

        _status.TotalItemsSynced += itemsSynced;
        _status.TotalErrors += errors;
    }

    public async Task<bool> TryAcquireSyncLock()
    {
        return await _syncLock.WaitAsync(0);
    }

    public void ReleaseSyncLock()
    {
        _syncLock.Release();
    }
}
