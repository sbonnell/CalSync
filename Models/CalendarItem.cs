namespace ExchangeCalendarSync.Models;

public class CalendarItemSync
{
    public string Id { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? Body { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string Location { get; set; } = string.Empty;
    public bool IsAllDay { get; set; }
    public List<string> RequiredAttendees { get; set; } = new();
    public List<string> OptionalAttendees { get; set; } = new();
    public string? Organizer { get; set; }
    public string? Categories { get; set; }
    public bool IsRecurring { get; set; }
    public string? RecurrencePattern { get; set; }
    public DateTime LastModified { get; set; }
    public string SourceMailbox { get; set; } = string.Empty;
    public string DestinationMailbox { get; set; } = string.Empty;
    public bool IsCancelled { get; set; }
    /// <summary>
    /// The display name of the mapping this item belongs to, used for log filtering.
    /// </summary>
    public string MappingName { get; set; } = string.Empty;
}
