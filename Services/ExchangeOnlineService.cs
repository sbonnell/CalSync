using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Identity.Client;
using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;
using Azure.Identity;

namespace ExchangeCalendarSync.Services;

public class ExchangeOnlineService : ICalendarDestinationService
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

    public virtual async Task<SyncResult> SyncCalendarItemAsync(CalendarItemSync item, bool forceUpdate = false)
    {
        if (_graphClient == null)
        {
            throw new InvalidOperationException("Service not initialized. Call Initialize() first.");
        }

        // Use DestinationMailbox if set, otherwise fall back to SourceMailbox for backward compatibility
        var targetMailbox = !string.IsNullOrEmpty(item.DestinationMailbox) ? item.DestinationMailbox : item.SourceMailbox;

        // Get mapping name for log prefix, fall back to source->dest if not set
        var mappingName = !string.IsNullOrEmpty(item.MappingName)
            ? item.MappingName
            : $"{item.SourceMailbox} -> {targetMailbox}";

        try
        {
            _logger.LogDebug("[{MappingName}] Syncing calendar item '{Subject}' from {Source} to {Destination}",
                mappingName, item.Subject, item.SourceMailbox, targetMailbox);

            // Check if item already exists and get its stored LastModified timestamp
            var (existingEvent, storedLastModified) = await FindExistingEventAsync(targetMailbox, item.Id);

            // Change detection: skip update if source hasn't been modified (unless forceUpdate is true)
            if (!forceUpdate && existingEvent != null && storedLastModified.HasValue)
            {
                var sourceLastModified = item.LastModified.ToUniversalTime();
                if (sourceLastModified <= storedLastModified.Value)
                {
                    _logger.LogDebug("[{MappingName}] No changes detected for '{Subject}', skipping update",
                        mappingName, item.Subject);
                    return SyncResult.NoChange;
                }
            }

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
                // Do NOT include attendees - Graph API sends invitation emails to all attendees
                // and there is no supported way to disable this for new events.
                // The destination room only needs the time blocked, not the attendee list.
                Categories = !string.IsNullOrEmpty(item.Categories)
                    ? item.Categories.Split(',').Select(c => c.Trim()).ToList()
                    : new List<string>(),
                // Store the source ID and LastModified in extended properties for tracking
                SingleValueExtendedProperties = new List<SingleValueLegacyExtendedProperty>
                {
                    new SingleValueLegacyExtendedProperty
                    {
                        Id = "String {00020329-0000-0000-C000-000000000046} Name SourceExchangeId",
                        Value = item.Id
                    },
                    new SingleValueLegacyExtendedProperty
                    {
                        Id = "String {00020329-0000-0000-C000-000000000046} Name SourceLastModified",
                        Value = item.LastModified.ToUniversalTime().ToString("o")
                    }
                }
            };

            if (existingEvent != null)
            {
                // Update existing event - disable sending update notifications to attendees
                await _graphClient.Users[targetMailbox]
                    .Events[existingEvent.Id]
                    .PatchAsync(graphEvent, requestConfiguration =>
                    {
                        requestConfiguration.Headers.Add("Prefer", "outlook.calendar.disableSendUpdates");
                    });

                _logger.LogInformation("[{MappingName}] Updated calendar item '{Subject}' for {Mailbox}", mappingName, item.Subject, targetMailbox);
                return SyncResult.Updated;
            }
            else
            {
                // Create new event - disable sending invitation notifications to attendees
                await _graphClient.Users[targetMailbox]
                    .Events
                    .PostAsync(graphEvent, requestConfiguration =>
                    {
                        requestConfiguration.Headers.Add("Prefer", "outlook.calendar.disableSendUpdates");
                    });

                _logger.LogInformation("[{MappingName}] Created calendar item '{Subject}' for {Mailbox}", mappingName, item.Subject, targetMailbox);
                return SyncResult.Created;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MappingName}] Failed to sync calendar item '{Subject}' to {Mailbox}", mappingName, item.Subject, targetMailbox);
            return SyncResult.Failed;
        }
    }

    private async Task<(Event? Event, DateTime? SourceLastModified)> FindExistingEventAsync(string userEmail, string sourceId)
    {
        try
        {
            if (_graphClient == null) return (null, null);

            // Search for events with the matching source ID in extended properties
            var filter = $"singleValueExtendedProperties/Any(ep: ep/id eq 'String {{00020329-0000-0000-C000-000000000046}} Name SourceExchangeId' and ep/value eq '{sourceId}')";

            var events = await _graphClient.Users[userEmail]
                .Events
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = filter;
                    requestConfiguration.QueryParameters.Top = 1;
                    // Expand to get both extended properties
                    requestConfiguration.QueryParameters.Expand = new[]
                    {
                        "singleValueExtendedProperties($filter=id eq 'String {00020329-0000-0000-C000-000000000046} Name SourceExchangeId' or id eq 'String {00020329-0000-0000-C000-000000000046} Name SourceLastModified')"
                    };
                });

            var existingEvent = events?.Value?.FirstOrDefault();
            if (existingEvent == null) return (null, null);

            // Extract the stored SourceLastModified timestamp
            DateTime? storedLastModified = null;
            var lastModifiedProp = existingEvent.SingleValueExtendedProperties?
                .FirstOrDefault(p => p.Id?.Contains("SourceLastModified") == true);

            if (lastModifiedProp?.Value != null && DateTime.TryParse(lastModifiedProp.Value, out var parsed))
            {
                storedLastModified = parsed.ToUniversalTime();
            }

            return (existingEvent, storedLastModified);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find existing event for source ID {SourceId}", sourceId);
            return (null, null);
        }
    }

    public async Task<bool> DeleteCalendarItemAsync(string destinationMailbox, string sourceId, string? mappingName = null)
    {
        if (_graphClient == null)
        {
            throw new InvalidOperationException("Service not initialized. Call Initialize() first.");
        }

        var logPrefix = !string.IsNullOrEmpty(mappingName) ? $"[{mappingName}] " : "";

        try
        {
            var (existingEvent, _) = await FindExistingEventAsync(destinationMailbox, sourceId);

            if (existingEvent == null)
            {
                _logger.LogDebug("{LogPrefix}No event found to delete for source ID {SourceId} in {Mailbox}", logPrefix, sourceId, destinationMailbox);
                return true; // Nothing to delete
            }

            await _graphClient.Users[destinationMailbox]
                .Events[existingEvent.Id]
                .DeleteAsync();

            _logger.LogInformation("{LogPrefix}Deleted calendar item with source ID {SourceId} from {Mailbox}", logPrefix, sourceId, destinationMailbox);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{LogPrefix}Failed to delete calendar item with source ID {SourceId} from {Mailbox}", logPrefix, sourceId, destinationMailbox);
            return false;
        }
    }

    public async Task<List<string>> GetSyncedSourceIdsAsync(string destinationMailbox, DateTime startDate, DateTime endDate, string? mappingName = null)
    {
        if (_graphClient == null)
        {
            throw new InvalidOperationException("Service not initialized. Call Initialize() first.");
        }

        var logPrefix = !string.IsNullOrEmpty(mappingName) ? $"[{mappingName}] " : "";
        var sourceIds = new List<string>();

        try
        {
            // Get all events in the date range that have our SourceExchangeId extended property
            var events = await _graphClient.Users[destinationMailbox]
                .CalendarView
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.StartDateTime = startDate.ToString("yyyy-MM-ddTHH:mm:ss");
                    requestConfiguration.QueryParameters.EndDateTime = endDate.ToString("yyyy-MM-ddTHH:mm:ss");
                    requestConfiguration.QueryParameters.Top = 1000;
                    requestConfiguration.QueryParameters.Expand = new[] { "singleValueExtendedProperties($filter=id eq 'String {00020329-0000-0000-C000-000000000046} Name SourceExchangeId')" };
                });

            if (events?.Value != null)
            {
                foreach (var evt in events.Value)
                {
                    var sourceIdProp = evt.SingleValueExtendedProperties?
                        .FirstOrDefault(p => p.Id?.Contains("SourceExchangeId") == true);

                    if (sourceIdProp?.Value != null)
                    {
                        sourceIds.Add(sourceIdProp.Value);
                    }
                }
            }

            _logger.LogDebug("{LogPrefix}Found {Count} synced events in {Mailbox}", logPrefix, sourceIds.Count, destinationMailbox);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{LogPrefix}Failed to get synced source IDs from {Mailbox}", logPrefix, destinationMailbox);
        }

        return sourceIds;
    }
}
