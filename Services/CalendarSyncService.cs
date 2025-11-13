using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public class CalendarSyncService
{
    private readonly ILogger<CalendarSyncService> _logger;
    private readonly ExchangeOnPremiseService _onPremiseService;
    private readonly ExchangeOnlineService _onlineService;
    private readonly SyncSettings _settings;
    private readonly SyncStatusService _statusService;
    private readonly Dictionary<string, DateTime> _lastSyncTimes;

    public CalendarSyncService(
        ILogger<CalendarSyncService> logger,
        ExchangeOnPremiseService onPremiseService,
        ExchangeOnlineService onlineService,
        SyncSettings settings,
        SyncStatusService statusService)
    {
        _logger = logger;
        _onPremiseService = onPremiseService;
        _onlineService = onlineService;
        _settings = settings;
        _statusService = statusService;
        _lastSyncTimes = new Dictionary<string, DateTime>();
    }

    // Legacy method for backward compatibility
    public async Task SyncAllMailboxesAsync(List<string> mailboxes)
    {
        var mappings = mailboxes.Select(m => new MailboxMapping
        {
            SourceMailbox = m,
            DestinationMailbox = m
        }).ToList();

        await SyncAllMailboxesAsync(mappings);
    }

    public async Task SyncAllMailboxesAsync(List<MailboxMapping> mailboxMappings)
    {
        _statusService.StartSync();
        _logger.LogInformation("Starting sync for {Count} mailbox mappings", mailboxMappings.Count);

        foreach (var mapping in mailboxMappings)
        {
            try
            {
                await SyncMailboxAsync(mapping);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync mailbox {Source} -> {Destination}",
                    mapping.SourceMailbox, mapping.DestinationMailbox);
                _statusService.UpdateMailboxStatus(
                    $"{mapping.SourceMailbox} → {mapping.DestinationMailbox}",
                    0, 1, "Failed");
            }
        }

        _logger.LogInformation("Completed sync for all mailboxes");
        _statusService.EndSync();
    }

    private async Task SyncMailboxAsync(MailboxMapping mapping)
    {
        var displayName = mapping.SourceMailbox == mapping.DestinationMailbox
            ? mapping.SourceMailbox
            : $"{mapping.SourceMailbox} → {mapping.DestinationMailbox}";

        try
        {
            _logger.LogInformation("Syncing mailbox: {Source} to {Destination}",
                mapping.SourceMailbox, mapping.DestinationMailbox);
            _statusService.UpdateMailboxStatus(displayName, 0, 0, "Syncing");

            // Determine the date range to sync
            var endDate = DateTime.UtcNow.AddDays(30); // Look ahead 30 days
            var startDate = _lastSyncTimes.ContainsKey(mapping.SourceMailbox)
                ? _lastSyncTimes[mapping.SourceMailbox].AddMinutes(-5) // Overlap by 5 minutes to catch updates
                : DateTime.UtcNow.AddDays(-_settings.LookbackDays); // Initial sync

            // Get calendar items from on-premise Exchange
            var calendarItems = await _onPremiseService.GetCalendarItemsAsync(
                mapping.SourceMailbox,
                startDate,
                endDate
            );

            if (!calendarItems.Any())
            {
                _logger.LogInformation("No calendar items to sync for {Source}", mapping.SourceMailbox);
                _lastSyncTimes[mapping.SourceMailbox] = DateTime.UtcNow;
                _statusService.UpdateMailboxStatus(displayName, 0, 0, "Completed");
                return;
            }

            _logger.LogInformation("Found {Count} calendar items to sync for {Source}",
                calendarItems.Count, mapping.SourceMailbox);

            // Sync each item to Exchange Online (one-way sync, no attachments)
            var successCount = 0;
            var failureCount = 0;

            foreach (var item in calendarItems)
            {
                try
                {
                    // Set the destination mailbox
                    item.DestinationMailbox = mapping.DestinationMailbox;

                    // Note: Attachments are not included in CalendarItemSync model
                    // This ensures attachments are never synced to Exchange Online
                    var success = await _onlineService.SyncCalendarItemAsync(item);

                    if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failureCount++;
                    }

                    // Small delay to avoid throttling
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync item '{Subject}' for {Source}",
                        item.Subject, mapping.SourceMailbox);
                    failureCount++;
                }
            }

            _logger.LogInformation(
                "Sync completed for {Source} -> {Destination}: {Success} succeeded, {Failures} failed",
                mapping.SourceMailbox, mapping.DestinationMailbox, successCount, failureCount);

            // Update last sync time and status
            _lastSyncTimes[mapping.SourceMailbox] = DateTime.UtcNow;
            _statusService.UpdateMailboxStatus(displayName, successCount, failureCount,
                failureCount > 0 ? "Completed with errors" : "Completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync mailbox {Source} -> {Destination}",
                mapping.SourceMailbox, mapping.DestinationMailbox);
            _statusService.UpdateMailboxStatus(displayName, 0, 1, "Failed");
            throw;
        }
    }
}
