using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public class CalendarSyncService : ICalendarSyncService
{
    private readonly ILogger<CalendarSyncService> _logger;
    private readonly ICalendarSourceService _onPremiseService;
    private readonly ICalendarSourceService _onlineSourceService;
    private readonly ICalendarDestinationService _onlineService;
    private readonly SyncSettings _settings;
    private readonly ISyncStatusService _statusService;
    private readonly ISyncStateRepository _stateRepository;
    private readonly ConcurrentDictionary<string, DateTime> _lastSyncTimes;
    private bool _stateLoaded;

    public CalendarSyncService(
        ILogger<CalendarSyncService> logger,
        ExchangeOnPremiseService onPremiseService,
        ExchangeOnlineSourceService onlineSourceService,
        ExchangeOnlineService onlineService,
        SyncSettings settings,
        ISyncStatusService statusService,
        ISyncStateRepository stateRepository)
    {
        _logger = logger;
        _onPremiseService = onPremiseService;
        _onlineSourceService = onlineSourceService;
        _onlineService = onlineService;
        _settings = settings;
        _statusService = statusService;
        _stateRepository = stateRepository;
        _lastSyncTimes = new ConcurrentDictionary<string, DateTime>();
        _stateLoaded = false;
    }

    private async Task EnsureStateLoadedAsync()
    {
        if (_stateLoaded) return;

        var state = await _stateRepository.LoadStateAsync();
        foreach (var kvp in state.LastSyncTimes)
        {
            _lastSyncTimes.TryAdd(kvp.Key, kvp.Value);
        }
        _stateLoaded = true;
        _logger.LogInformation("Loaded {Count} sync times from persisted state", state.LastSyncTimes.Count);
    }

    private async Task PersistStateAsync()
    {
        var state = new PersistedSyncState
        {
            LastSyncTimes = new Dictionary<string, DateTime>(_lastSyncTimes)
        };
        await _stateRepository.SaveStateAsync(state);
    }

    // Legacy method for backward compatibility
    public virtual async Task SyncAllMailboxesAsync(List<string> mailboxes)
    {
        var mappings = mailboxes.Select(m => new MailboxMapping
        {
            SourceMailbox = m,
            DestinationMailbox = m,
            SourceType = SourceType.ExchangeOnPremise
        }).ToList();

        await SyncAllMailboxesAsync(mappings);
    }

    public virtual async Task SyncAllMailboxesAsync(List<MailboxMapping> mailboxMappings)
    {
        await EnsureStateLoadedAsync();

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

        // Persist state after sync completes
        await PersistStateAsync();

        _logger.LogInformation("Completed sync for all mailboxes");
        _statusService.EndSync();
    }

    private async Task SyncMailboxAsync(MailboxMapping mapping)
    {
        var sourceTypeLabel = mapping.SourceType == SourceType.ExchangeOnPremise ? "EWS" : "Graph";
        var displayName = mapping.SourceMailbox == mapping.DestinationMailbox
            ? $"{mapping.SourceMailbox} ({sourceTypeLabel})"
            : $"{mapping.SourceMailbox} → {mapping.DestinationMailbox} ({sourceTypeLabel})";

        try
        {
            _logger.LogInformation("Syncing mailbox: {Source} ({SourceType}) to {Destination}",
                mapping.SourceMailbox, sourceTypeLabel, mapping.DestinationMailbox);
            _statusService.UpdateMailboxStatus(displayName, 0, 0, "Syncing");

            // Determine the date range to sync
            var endDate = DateTime.UtcNow.AddDays(30); // Look ahead 30 days
            var startDate = _lastSyncTimes.TryGetValue(mapping.SourceMailbox, out var lastSync)
                ? lastSync.AddMinutes(-5) // Overlap by 5 minutes to catch updates
                : DateTime.UtcNow.AddDays(-_settings.LookbackDays); // Initial sync

            // Get calendar items from appropriate source based on SourceType
            List<CalendarItemSync> calendarItems;

            if (mapping.SourceType == SourceType.ExchangeOnline)
            {
                // Fetch from Exchange Online via Graph API
                calendarItems = await _onlineSourceService.GetCalendarItemsAsync(
                    mapping.SourceMailbox,
                    startDate,
                    endDate
                );
            }
            else
            {
                // Fetch from Exchange On-Premise via EWS
                calendarItems = await _onPremiseService.GetCalendarItemsAsync(
                    mapping.SourceMailbox,
                    startDate,
                    endDate
                );
            }

            if (!calendarItems.Any())
            {
                _logger.LogInformation("No calendar items to sync for {Source}", mapping.SourceMailbox);
                _lastSyncTimes[mapping.SourceMailbox] = DateTime.UtcNow;
                _statusService.UpdateMailboxStatus(displayName, 0, 0, "Completed");
                return;
            }

            _logger.LogInformation("Found {Count} calendar items to sync for {Source}",
                calendarItems.Count, mapping.SourceMailbox);

            // Sync each item to destination (one-way sync, no attachments)
            var successCount = 0;
            var failureCount = 0;

            foreach (var item in calendarItems)
            {
                try
                {
                    // Set the destination mailbox
                    item.DestinationMailbox = mapping.DestinationMailbox;

                    // Note: Attachments are not included in CalendarItemSync model
                    // This ensures attachments are never synced
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
