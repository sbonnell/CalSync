using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Identity.Client;
using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;
using Azure.Identity;

namespace ExchangeCalendarSync.Services;

public class ExchangeOnlineService
{
    private readonly ILogger<ExchangeOnlineService> _logger;
    private readonly ExchangeOnlineSettings _settings;
    private GraphServiceClient? _graphClient;

    public ExchangeOnlineService(
        ILogger<ExchangeOnlineService> logger,
        ExchangeOnlineSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public void Initialize()
    {
        try
        {
            var clientSecretCredential = new ClientSecretCredential(
                _settings.TenantId,
                _settings.ClientId,
                _settings.ClientSecret
            );

            _graphClient = new GraphServiceClient(clientSecretCredential);

            _logger.LogInformation("Exchange Online service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Exchange Online service");
            throw;
        }
    }

    public async Task<bool> SyncCalendarItemAsync(CalendarItemSync item)
    {
        if (_graphClient == null)
        {
            throw new InvalidOperationException("Service not initialized. Call Initialize() first.");
        }

        // Use DestinationMailbox if set, otherwise fall back to SourceMailbox for backward compatibility
        var targetMailbox = !string.IsNullOrEmpty(item.DestinationMailbox) ? item.DestinationMailbox : item.SourceMailbox;

        try
        {
            _logger.LogInformation("Syncing calendar item '{Subject}' from {Source} to {Destination}",
                item.Subject, item.SourceMailbox, targetMailbox);

            // Check if item already exists
            var existingEvent = await FindExistingEventAsync(targetMailbox, item.Id);

            var graphEvent = new Event
            {
                Subject = item.Subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = item.Body ?? string.Empty
                },
                Start = new DateTimeTimeZone
                {
                    DateTime = item.Start.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "UTC"
                },
                End = new DateTimeTimeZone
                {
                    DateTime = item.End.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "UTC"
                },
                Location = new Location
                {
                    DisplayName = item.Location
                },
                IsAllDay = item.IsAllDay,
                Attendees = CreateAttendees(item),
                Categories = !string.IsNullOrEmpty(item.Categories)
                    ? item.Categories.Split(',').Select(c => c.Trim()).ToList()
                    : new List<string>(),
                // Store the source ID in extended properties for tracking
                SingleValueExtendedProperties = new List<SingleValueLegacyExtendedProperty>
                {
                    new SingleValueLegacyExtendedProperty
                    {
                        Id = "String {00020329-0000-0000-C000-000000000046} Name SourceExchangeId",
                        Value = item.Id
                    }
                }
            };

            if (existingEvent != null)
            {
                // Update existing event
                await _graphClient.Users[targetMailbox]
                    .Events[existingEvent.Id]
                    .PatchAsync(graphEvent);

                _logger.LogInformation("Updated calendar item '{Subject}' for {Mailbox}", item.Subject, targetMailbox);
            }
            else
            {
                // Create new event
                await _graphClient.Users[targetMailbox]
                    .Events
                    .PostAsync(graphEvent);

                _logger.LogInformation("Created calendar item '{Subject}' for {Mailbox}", item.Subject, targetMailbox);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync calendar item '{Subject}' to {Mailbox}", item.Subject, targetMailbox);
            return false;
        }
    }

    private async Task<Event?> FindExistingEventAsync(string userEmail, string sourceId)
    {
        try
        {
            if (_graphClient == null) return null;

            // Search for events with the matching source ID in extended properties
            var filter = $"singleValueExtendedProperties/Any(ep: ep/id eq 'String {{00020329-0000-0000-C000-000000000046}} Name SourceExchangeId' and ep/value eq '{sourceId}')";

            var events = await _graphClient.Users[userEmail]
                .Events
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = filter;
                    requestConfiguration.QueryParameters.Top = 1;
                });

            return events?.Value?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find existing event for source ID {SourceId}", sourceId);
            return null;
        }
    }

    private List<Attendee> CreateAttendees(CalendarItemSync item)
    {
        var attendees = new List<Attendee>();

        foreach (var email in item.RequiredAttendees)
        {
            attendees.Add(new Attendee
            {
                EmailAddress = new EmailAddress { Address = email },
                Type = AttendeeType.Required
            });
        }

        foreach (var email in item.OptionalAttendees)
        {
            attendees.Add(new Attendee
            {
                EmailAddress = new EmailAddress { Address = email },
                Type = AttendeeType.Optional
            });
        }

        return attendees;
    }
}
