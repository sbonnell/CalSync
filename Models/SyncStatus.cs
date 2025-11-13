namespace ExchangeCalendarSync.Models;

public class SyncStatus
{
    public DateTime? LastSyncTime { get; set; }
    public DateTime? NextScheduledSync { get; set; }
    public bool IsRunning { get; set; }
    public Dictionary<string, MailboxSyncStatus> MailboxStatuses { get; set; } = new();
    public int TotalItemsSynced { get; set; }
    public int TotalErrors { get; set; }
}

public class MailboxSyncStatus
{
    public string MailboxEmail { get; set; } = string.Empty;
    public DateTime? LastSyncTime { get; set; }
    public int ItemsSynced { get; set; }
    public int Errors { get; set; }
    public string Status { get; set; } = "Pending";
}
