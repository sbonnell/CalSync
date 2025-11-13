namespace ExchangeCalendarSync.Models;

public class AppSettings
{
    public ExchangeOnPremiseSettings ExchangeOnPremise { get; set; } = new();
    public ExchangeOnlineSettings ExchangeOnline { get; set; } = new();
    public SyncSettings Sync { get; set; } = new();
}

public class MailboxMapping
{
    public string SourceMailbox { get; set; } = string.Empty;
    public string DestinationMailbox { get; set; } = string.Empty;
}

public class ExchangeOnPremiseSettings
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;

    // Legacy format: list of mailboxes (source and destination are the same)
    public List<string> MailboxesToMonitor { get; set; } = new();

    // New format: explicit source-to-destination mapping
    public List<MailboxMapping> MailboxMappings { get; set; } = new();

    // Helper method to get all mappings (handles both legacy and new format)
    public List<MailboxMapping> GetMailboxMappings()
    {
        var mappings = new List<MailboxMapping>();

        // Add explicit mappings from new format
        mappings.AddRange(MailboxMappings);

        // Add legacy format mappings (where source == destination)
        foreach (var mailbox in MailboxesToMonitor)
        {
            // Don't add duplicates
            if (!mappings.Any(m => m.SourceMailbox.Equals(mailbox, StringComparison.OrdinalIgnoreCase)))
            {
                mappings.Add(new MailboxMapping
                {
                    SourceMailbox = mailbox,
                    DestinationMailbox = mailbox
                });
            }
        }

        return mappings;
    }
}

public class ExchangeOnlineSettings
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public class SyncSettings
{
    public int SyncIntervalMinutes { get; set; } = 5;
    public int LookbackDays { get; set; } = 30;
}
