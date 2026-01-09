namespace ExchangeCalendarSync.Models;

public class PersistedSyncState
{
    public Dictionary<string, DateTime> LastSyncTimes { get; set; } = new();
    public DateTime? LastPersistedAt { get; set; }
}
