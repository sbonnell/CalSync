using System.Collections.Concurrent;
using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public class SyncStatusService : ISyncStatusService
{
    private readonly SyncStatus _status;
    private readonly SemaphoreSlim _syncLock;
    private readonly object _statusLock = new();

    public SyncStatusService()
    {
        _status = new SyncStatus
        {
            MailboxStatuses = new ConcurrentDictionary<string, MailboxSyncStatus>()
        };
        _syncLock = new SemaphoreSlim(1, 1);
    }

    public SyncStatus GetStatus()
    {
        lock (_statusLock)
        {
            // Return a snapshot to avoid external modifications
            return new SyncStatus
            {
                LastSyncTime = _status.LastSyncTime,
                NextScheduledSync = _status.NextScheduledSync,
                IsRunning = _status.IsRunning,
                TotalItemsSynced = _status.TotalItemsSynced,
                TotalErrors = _status.TotalErrors,
                MailboxStatuses = new ConcurrentDictionary<string, MailboxSyncStatus>(_status.MailboxStatuses)
            };
        }
    }

    public bool IsRunning()
    {
        lock (_statusLock)
        {
            return _status.IsRunning;
        }
    }

    public void StartSync()
    {
        lock (_statusLock)
        {
            _status.IsRunning = true;
        }
    }

    public void EndSync()
    {
        lock (_statusLock)
        {
            _status.IsRunning = false;
            _status.LastSyncTime = DateTime.UtcNow;
        }
    }

    public void SetNextScheduledSync(DateTime nextSync)
    {
        lock (_statusLock)
        {
            _status.NextScheduledSync = nextSync;
        }
    }

    public void UpdateMailboxStatus(string mailbox, int itemsSynced, int errors, string status)
    {
        lock (_statusLock)
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
