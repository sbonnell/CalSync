using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public interface ICalendarDestinationService
{
    void Initialize();
    Task<SyncResult> SyncCalendarItemAsync(CalendarItemSync item, bool forceUpdate = false);
    Task<bool> DeleteCalendarItemAsync(string destinationMailbox, string sourceId, string? mappingName = null);
    Task<List<string>> GetSyncedSourceIdsAsync(string destinationMailbox, DateTime startDate, DateTime endDate, string? mappingName = null);
}
