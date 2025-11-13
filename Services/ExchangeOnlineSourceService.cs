using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;
using Azure.Identity;

namespace ExchangeCalendarSync.Services;

public class ExchangeOnlineSourceService
{
    private readonly ILogger<ExchangeOnlineSourceService> _logger;
    private readonly ExchangeOnlineSourceSettings? _settings;
    private GraphServiceClient? _graphClient;

    public ExchangeOnlineSourceService(
        ILogger<ExchangeOnlineSourceService> logger,
        ExchangeOnlineSourceSettings? settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public void Initialize()
    {
        if (_settings == null)
        {
            _logger.LogInformation("Exchange Online Source service not configured - skipping initialization");
            return;
        }

        try
        {
            var clientSecretCredential = new ClientSecretCredential(
                _settings.TenantId,
                _settings.ClientId,
                _settings.ClientSecret
            );

            _graphClient = new GraphServiceClient(clientSecretCredential);

            _logger.LogInformation("Exchange Online Source service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Exchange Online Source service");
            throw;
        }
    }

    public virtual async Task<List<CalendarItemSync>> GetCalendarItemsAsync(string mailboxEmail, DateTime startDate, DateTime endDate)
    {
        if (_graphClient == null)
        {
            throw new InvalidOperationException("Service not initialized. Call Initialize() first.");
        }

        var items = new List<CalendarItemSync>();

        try
        {
            _logger.LogInformation("Fetching calendar items for {Mailbox} from {Start} to {End}",
                mailboxEmail, startDate, endDate);

            // Build the filter for date range
            var filter = $"start/dateTime ge '{startDate:yyyy-MM-ddTHH:mm:ss}' and end/dateTime le '{endDate:yyyy-MM-ddTHH:mm:ss}'";

            var events = await _graphClient.Users[mailboxEmail]
                .CalendarView
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.StartDateTime = startDate.ToString("yyyy-MM-ddTHH:mm:ss");
                    requestConfiguration.QueryParameters.EndDateTime = endDate.ToString("yyyy-MM-ddTHH:mm:ss");
                    requestConfiguration.QueryParameters.Top = 1000; // Adjust as needed
                });

            if (events?.Value == null || !events.Value.Any())
            {
                _logger.LogInformation("No calendar items found for {Mailbox}", mailboxEmail);
                return items;
            }

            foreach (var evt in events.Value)
            {
                try
                {
                    var item = new CalendarItemSync
                    {
                        Id = evt.Id ?? string.Empty,
                        Subject = evt.Subject ?? string.Empty,
                        Body = evt.Body?.Content,
                        Start = evt.Start != null ? DateTime.Parse(evt.Start.DateTime) : DateTime.MinValue,
                        End = evt.End != null ? DateTime.Parse(evt.End.DateTime) : DateTime.MinValue,
                        Location = evt.Location?.DisplayName ?? string.Empty,
                        IsAllDay = evt.IsAllDay ?? false,
                        RequiredAttendees = evt.Attendees?
                            .Where(a => a.Type == AttendeeType.Required)
                            .Select(a => a.EmailAddress?.Address ?? string.Empty)
                            .Where(e => !string.IsNullOrEmpty(e))
                            .ToList() ?? new List<string>(),
                        OptionalAttendees = evt.Attendees?
                            .Where(a => a.Type == AttendeeType.Optional)
                            .Select(a => a.EmailAddress?.Address ?? string.Empty)
                            .Where(e => !string.IsNullOrEmpty(e))
                            .ToList() ?? new List<string>(),
                        Organizer = evt.Organizer?.EmailAddress?.Address,
                        Categories = evt.Categories != null && evt.Categories.Any()
                            ? string.Join(", ", evt.Categories)
                            : null,
                        IsRecurring = evt.Recurrence != null,
                        RecurrencePattern = evt.Recurrence?.Pattern?.ToString(),
                        LastModified = evt.LastModifiedDateTime?.DateTime ?? DateTime.UtcNow,
                        SourceMailbox = mailboxEmail
                    };

                    items.Add(item);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process calendar item: {Subject}", evt.Subject);
                }
            }

            _logger.LogInformation("Retrieved {Count} calendar items for {Mailbox}", items.Count, mailboxEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch calendar items for {Mailbox}", mailboxEmail);
            throw;
        }

        return items;
    }
}
