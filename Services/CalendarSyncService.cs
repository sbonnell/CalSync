using System.Collections.Concurrent;
using System.Diagnostics;
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
        await SyncAllMailboxesAsync(mailboxMappings, forceFullSync: false);
    }

    public virtual async Task SyncAllMailboxesAsync(List<MailboxMapping> mailboxMappings, bool forceFullSync)
    {
        await EnsureStateLoadedAsync();

        // Note: forceFullSync now only affects whether change detection is bypassed
        // (items are force-updated even if LastModified hasn't changed)
        // The date range is always the full lookback/lookforward window

        var stopwatch = Stopwatch.StartNew();
        _statusService.StartSync();
        var syncType = forceFullSync ? "full" : "incremental";
        _logger.LogInformation("Starting {SyncType} sync for {Count} mailbox mappings", syncType, mailboxMappings.Count);

        foreach (var mapping in mailboxMappings)
        {
            var displayName = mapping.GetDisplayName();
            try
            {
                await SyncMailboxAsync(mapping, forceFullSync);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{MappingName}] Failed to sync mailbox {Source} -> {Destination}",
                    displayName, mapping.SourceMailbox, mapping.DestinationMailbox);
                _statusService.UpdateMailboxStatus(displayName, 0, 1, "Failed");
            }
        }

        // Persist state after sync completes
        await PersistStateAsync();

        stopwatch.Stop();
        var elapsed = stopwatch.Elapsed;
        var duration = elapsed.TotalHours >= 1
            ? $"{elapsed.Hours}h{elapsed.Minutes}m{elapsed.Seconds}s"
            : elapsed.TotalMinutes >= 1
                ? $"{elapsed.Minutes}m{elapsed.Seconds}s"
                : $"{elapsed.TotalSeconds:F1}s";
        _logger.LogInformation("Completed {SyncType} sync for all mailboxes in {Duration}", syncType, duration);
        _statusService.EndSync();
    }

    private async Task SyncMailboxAsync(MailboxMapping mapping, bool forceUpdate = false)
    {
        var displayName = mapping.GetDisplayName();

        try
        {
            _logger.LogInformation("[{MappingName}] Starting sync: {Source} -> {Destination}",
                displayName, mapping.SourceMailbox, mapping.DestinationMailbox);
            _statusService.UpdateMailboxStatus(displayName, 0, 0, "Syncing");

            // Determine the date range to sync - always use full lookback/lookforward range
            // This ensures we catch all items in the configured window regardless of sync type
            var endDate = DateTime.UtcNow.AddDays(_settings.LookForwardDays);
            var startDate = DateTime.UtcNow.AddDays(-_settings.LookbackDays);

            // Get calendar items from appropriate source based on SourceType
            List<CalendarItemSync> calendarItems;

            if (mapping.SourceType == SourceType.ExchangeOnline)
            {
                // Fetch from Exchange Online via Graph API
                calendarItems = await _onlineSourceService.GetCalendarItemsAsync(
                    mapping.SourceMailbox,
                    startDate,
                    endDate,
                    displayName
                );
            }
            else
            {
                // Fetch from Exchange On-Premise via EWS
                calendarItems = await _onPremiseService.GetCalendarItemsAsync(
                    mapping.SourceMailbox,
                    startDate,
                    endDate,
                    displayName
                );
            }

            // Separate cancelled items from active items
            var cancelledItems = calendarItems.Where(i => i.IsCancelled).ToList();
            var activeItems = calendarItems.Where(i => !i.IsCancelled).ToList();

            _logger.LogInformation("[{MappingName}] Found {ActiveCount} active and {CancelledCount} cancelled calendar items",
                displayName, activeItems.Count, cancelledItems.Count);

            // Sync each active item to destination (one-way sync, no attachments)
            var createdCount = 0;
            var updatedCount = 0;
            var noChangeCount = 0;
            var failureCount = 0;

            foreach (var item in activeItems)
            {
                try
                {
                    // Set the destination mailbox and mapping name for logging
                    item.DestinationMailbox = mapping.DestinationMailbox;
                    item.MappingName = displayName;

                    // Note: Attachments are not included in CalendarItemSync model
                    // This ensures attachments are never synced
                    var result = await _onlineService.SyncCalendarItemAsync(item, forceUpdate);

                    switch (result)
                    {
                        case SyncResult.Created:
                            createdCount++;
                            break;
                        case SyncResult.Updated:
                            updatedCount++;
                            break;
                        case SyncResult.NoChange:
                            noChangeCount++;
                            break;
                        case SyncResult.Failed:
                            failureCount++;
                            break;
                    }

                    // Small delay to avoid throttling
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{MappingName}] Failed to sync item '{Subject}'",
                        displayName, item.Subject);
                    failureCount++;
                }
            }

            // Delete cancelled items from destination
            foreach (var cancelledItem in cancelledItems)
            {
                try
                {
                    _logger.LogInformation("[{MappingName}] Deleting cancelled item '{Subject}'",
                        displayName, cancelledItem.Subject);

                    var deleted = await _onlineService.DeleteCalendarItemAsync(
                        mapping.DestinationMailbox,
                        cancelledItem.Id,
                        displayName);

                    if (deleted)
                    {
                        updatedCount++; // Deletions count as updates
                    }
                    else
                    {
                        failureCount++;
                    }

                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{MappingName}] Failed to delete cancelled item '{Subject}'",
                        displayName, cancelledItem.Subject);
                    failureCount++;
                }
            }

            // Handle deletions: find items in destination that no longer exist in source
            var (deletionSuccesses, deletionFailures) = await SyncDeletionsAsync(mapping, startDate, endDate, activeItems);
            updatedCount += deletionSuccesses; // Deletions count as updates
            failureCount += deletionFailures;

            var evaluatedCount = activeItems.Count + cancelledItems.Count;
            _logger.LogInformation(
                "[{MappingName}] Sync completed: {Evaluated} evaluated, {Created} created, {Updated} updated, {NoChange} unchanged, {Failures} failed",
                displayName, evaluatedCount, createdCount, updatedCount, noChangeCount, failureCount);

            // Update last sync time and status with detailed counts
            _lastSyncTimes[mapping.SourceMailbox] = DateTime.UtcNow;
            _statusService.UpdateMailboxStatus(displayName, evaluatedCount, createdCount, updatedCount, noChangeCount, failureCount,
                failureCount > 0 ? "Completed with errors" : "Completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MappingName}] Failed to sync mailbox {Source} -> {Destination}",
                displayName, mapping.SourceMailbox, mapping.DestinationMailbox);
            _statusService.UpdateMailboxStatus(displayName, 0, 1, "Failed");
            throw;
        }
    }

    private async Task<(int successCount, int failureCount)> SyncDeletionsAsync(
        MailboxMapping mapping,
        DateTime startDate,
        DateTime endDate,
        List<CalendarItemSync> sourceItems)
    {
        var displayName = mapping.GetDisplayName();
        var successCount = 0;
        var failureCount = 0;

        try
        {
            // Get all synced source IDs from the destination calendar
            var destinationSourceIds = await _onlineService.GetSyncedSourceIdsAsync(
                mapping.DestinationMailbox,
                startDate,
                endDate,
                displayName);

            if (!destinationSourceIds.Any())
            {
                return (successCount, failureCount);
            }

            // Find source IDs that exist in destination but not in source (deleted items)
            var sourceItemIds = new HashSet<string>(sourceItems.Select(i => i.Id));
            var deletedSourceIds = destinationSourceIds.Where(id => !sourceItemIds.Contains(id)).ToList();

            if (!deletedSourceIds.Any())
            {
                _logger.LogDebug("[{MappingName}] No deleted items to remove", displayName);
                return (successCount, failureCount);
            }

            _logger.LogInformation("[{MappingName}] Found {Count} items to delete (no longer exist in source)",
                displayName, deletedSourceIds.Count);

            foreach (var sourceId in deletedSourceIds)
            {
                try
                {
                    var deleted = await _onlineService.DeleteCalendarItemAsync(
                        mapping.DestinationMailbox,
                        sourceId,
                        displayName);

                    if (deleted)
                    {
                        successCount++;
                    }
                    else
                    {
                        failureCount++;
                    }

                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{MappingName}] Failed to delete orphaned item with source ID {SourceId}",
                        displayName, sourceId);
                    failureCount++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{MappingName}] Failed to sync deletions", displayName);
        }

        return (successCount, failureCount);
    }
}
