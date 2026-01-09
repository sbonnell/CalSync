using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public interface ICalendarDestinationService
{
    void Initialize();
    Task<bool> SyncCalendarItemAsync(CalendarItemSync item);
}
