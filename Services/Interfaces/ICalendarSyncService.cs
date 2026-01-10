using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public interface ICalendarSyncService
{
    Task SyncAllMailboxesAsync(List<string> mailboxes);
    Task SyncAllMailboxesAsync(List<MailboxMapping> mailboxMappings);
    Task SyncAllMailboxesAsync(List<MailboxMapping> mailboxMappings, bool forceFullSync);
}
