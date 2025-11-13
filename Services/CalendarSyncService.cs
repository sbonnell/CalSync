using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public class CalendarSyncService
{
    private readonly ILogger<CalendarSyncService> _logger;
    private readonly ExchangeOnPremiseService _onPremiseService;
    private readonly ExchangeOnlineService _onlineService;
    private readonly SyncSettings _settings;
    private readonly Dictionary<string, DateTime> _lastSyncTimes;

    public CalendarSyncService(
        ILogger<CalendarSyncService> logger,
        ExchangeOnPremiseService onPremiseService,
        ExchangeOnlineService onlineService,
        SyncSettings settings)
    {
        _logger = logger;
        _onPremiseService = onPremiseService;
        _onlineService = onlineService;
        _settings = settings;
        _lastSyncTimes = new Dictionary<string, DateTime>();
    }

    public async Task SyncAllMailboxesAsync(List<string> mailboxes)
    {
        _logger.LogInformation("Starting sync for {Count} mailboxes", mailboxes.Count);

        foreach (var mailbox in mailboxes)
        {
            try
            {
                await SyncMailboxAsync(mailbox);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync mailbox {Mailbox}", mailbox);
            }
        }

        _logger.LogInformation("Completed sync for all mailboxes");
    }

    private async Task SyncMailboxAsync(string mailboxEmail)
    {
        try
        {
            _logger.LogInformation("Syncing mailbox: {Mailbox}", mailboxEmail);

            // Determine the date range to sync
            var endDate = DateTime.UtcNow.AddDays(30); // Look ahead 30 days
            var startDate = _lastSyncTimes.ContainsKey(mailboxEmail)
                ? _lastSyncTimes[mailboxEmail].AddMinutes(-5) // Overlap by 5 minutes to catch updates
                : DateTime.UtcNow.AddDays(-_settings.LookbackDays); // Initial sync

            // Get calendar items from on-premise Exchange
            var calendarItems = await _onPremiseService.GetCalendarItemsAsync(
                mailboxEmail,
                startDate,
                endDate
            );

            if (!calendarItems.Any())
            {
                _logger.LogInformation("No calendar items to sync for {Mailbox}", mailboxEmail);
                _lastSyncTimes[mailboxEmail] = DateTime.UtcNow;
                return;
            }

            _logger.LogInformation("Found {Count} calendar items to sync for {Mailbox}",
                calendarItems.Count, mailboxEmail);

            // Sync each item to Exchange Online (one-way sync, no attachments)
            var successCount = 0;
            var failureCount = 0;

            foreach (var item in calendarItems)
            {
                try
                {
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
                    _logger.LogError(ex, "Failed to sync item '{Subject}' for {Mailbox}",
                        item.Subject, mailboxEmail);
                    failureCount++;
                }
            }

            _logger.LogInformation(
                "Sync completed for {Mailbox}: {Success} succeeded, {Failures} failed",
                mailboxEmail, successCount, failureCount);

            // Update last sync time
            _lastSyncTimes[mailboxEmail] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync mailbox {Mailbox}", mailboxEmail);
            throw;
        }
    }
}
