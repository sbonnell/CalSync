using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public interface ICalendarSourceService
{
    void Initialize();
    Task<List<CalendarItemSync>> GetCalendarItemsAsync(string mailboxEmail, DateTime startDate, DateTime endDate, string? mappingName = null);
}
