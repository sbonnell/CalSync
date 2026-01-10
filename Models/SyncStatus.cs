using System.Collections.Concurrent;

namespace ExchangeCalendarSync.Models;

public class SyncStatus
{
    public DateTime? LastSyncTime { get; set; }
    public DateTime? NextScheduledSync { get; set; }
    public bool IsRunning { get; set; }
    public bool IsSyncEnabled { get; set; } = true;
    public IDictionary<string, MailboxSyncStatus> MailboxStatuses { get; set; } = new ConcurrentDictionary<string, MailboxSyncStatus>();
    public int TotalItemsSynced { get; set; }
    public int TotalErrors { get; set; }
}

public class MailboxSyncStatus
{
    public string MailboxEmail { get; set; } = string.Empty;
    public DateTime? LastSyncTime { get; set; }
    public int ItemsSynced { get; set; }
    public int ItemsEvaluated { get; set; }
    public int ItemsCreated { get; set; }
    public int ItemsUpdated { get; set; }
    public int ItemsUnchanged { get; set; }
    public int Errors { get; set; }
    public string Status { get; set; } = "Pending";
}
