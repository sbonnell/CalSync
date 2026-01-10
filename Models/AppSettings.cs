namespace ExchangeCalendarSync.Models;

public enum SourceType
{
    ExchangeOnPremise,  // Exchange 2019 via EWS
    ExchangeOnline      // Exchange Online via Graph API
}

public enum SyncResult
{
    Created,    // New item was created
    Updated,    // Existing item was updated
    NoChange,   // Item exists but no changes detected
    Failed      // Sync failed
}

public class AppSettings
{
    public ExchangeOnPremiseSettings ExchangeOnPremise { get; set; } = new();
    public ExchangeOnlineSettings ExchangeOnline { get; set; } = new();
    public ExchangeOnlineSourceSettings? ExchangeOnlineSource { get; set; }
    public SyncSettings Sync { get; set; } = new();
    public PersistenceSettings Persistence { get; set; } = new();
    public OpenTelemetrySettings OpenTelemetry { get; set; } = new();
}

public class MailboxMapping
{
    /// <summary>
    /// Short friendly name for this mapping (e.g., "Alpha to Beta"). Used in logs and UI.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    public string SourceMailbox { get; set; } = string.Empty;
    public string DestinationMailbox { get; set; } = string.Empty;
    public SourceType SourceType { get; set; } = SourceType.ExchangeOnPremise;

    /// <summary>
    /// Returns the display name, falling back to a generated name if not set.
    /// </summary>
    public string GetDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(Name))
            return Name;

        // Generate a name from source/destination if not provided
        var source = SourceMailbox.Split('@')[0];
        var dest = DestinationMailbox.Split('@')[0];
        return $"{source} -> {dest}";
    }
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

        // Add legacy format mappings (where source == destination, from Exchange On-Premise)
        foreach (var mailbox in MailboxesToMonitor)
        {
            // Don't add duplicates
            if (!mappings.Any(m => m.SourceMailbox.Equals(mailbox, StringComparison.OrdinalIgnoreCase)))
            {
                mappings.Add(new MailboxMapping
                {
                    SourceMailbox = mailbox,
                    DestinationMailbox = mailbox,
                    SourceType = SourceType.ExchangeOnPremise
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

// Settings for Exchange Online as a SOURCE (separate from destination)
public class ExchangeOnlineSourceSettings
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public class SyncSettings
{
    public int SyncIntervalMinutes { get; set; } = 5;
    public int LookbackDays { get; set; } = 30;
    public int LookForwardDays { get; set; } = 30;
}

public class PersistenceSettings
{
    public string DataPath { get; set; } = "./data";
    public bool EnableStatePersistence { get; set; } = true;
}

public class OpenTelemetrySettings
{
    /// <summary>
    /// Enable or disable OpenTelemetry export.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// OTLP base endpoint URL (e.g., http://otel-collector:4317 for gRPC or https://ingest.signoz.cloud:443 for HTTP).
    /// For gRPC, this is used directly. For HTTP, /v1/logs and /v1/metrics paths are appended automatically.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:4317";

    /// <summary>
    /// Service name for OTEL resource identification.
    /// </summary>
    public string ServiceName { get; set; } = "exchange-calendar-sync";

    /// <summary>
    /// Environment name (e.g., production, staging, development).
    /// </summary>
    public string Environment { get; set; } = "production";

    /// <summary>
    /// Enable logging export to OTLP.
    /// </summary>
    public bool ExportLogs { get; set; } = true;

    /// <summary>
    /// Enable metrics export to OTLP.
    /// </summary>
    public bool ExportMetrics { get; set; } = true;

    /// <summary>
    /// Protocol to use: "grpc" or "http" (HTTP/Protobuf).
    /// The port should be specified in the Endpoint URL, not derived from protocol.
    /// For SigNoz cloud (HTTPS on port 443), use Protocol="http" with Endpoint="https://ingest.{region}.signoz.cloud:443/v1/..."
    /// </summary>
    public string Protocol { get; set; } = "grpc";

    /// <summary>
    /// Headers for OTLP authentication (comma-separated key=value pairs).
    /// Example: "api-key=xyz123,x-custom-header=value"
    /// </summary>
    public string? Headers { get; set; }

    /// <summary>
    /// Metrics export interval in seconds.
    /// </summary>
    public int MetricsExportIntervalSeconds { get; set; } = 60;
}
