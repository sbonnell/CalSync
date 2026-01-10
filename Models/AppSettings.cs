using System.ComponentModel.DataAnnotations;

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

/// <summary>
/// Common interface for Exchange Online credential settings.
/// Implemented by both ExchangeOnlineSettings (destination) and ExchangeOnlineSourceSettings (source).
/// </summary>
public interface IExchangeOnlineCredentials
{
    string TenantId { get; }
    string ClientId { get; }
    string ClientSecret { get; }
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

    [Required(ErrorMessage = "SourceMailbox is required")]
    [EmailAddress(ErrorMessage = "SourceMailbox must be a valid email address")]
    public string SourceMailbox { get; set; } = string.Empty;

    [Required(ErrorMessage = "DestinationMailbox is required")]
    [EmailAddress(ErrorMessage = "DestinationMailbox must be a valid email address")]
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
    [Url(ErrorMessage = "ServerUrl must be a valid URL")]
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

public class ExchangeOnlineSettings : IExchangeOnlineCredentials
{
    [Required(ErrorMessage = "TenantId is required for Exchange Online destination")]
    public string TenantId { get; set; } = string.Empty;

    [Required(ErrorMessage = "ClientId is required for Exchange Online destination")]
    public string ClientId { get; set; } = string.Empty;

    [Required(ErrorMessage = "ClientSecret is required for Exchange Online destination")]
    public string ClientSecret { get; set; } = string.Empty;
}

// Settings for Exchange Online as a SOURCE (separate from destination)
public class ExchangeOnlineSourceSettings : IExchangeOnlineCredentials
{
    [Required(ErrorMessage = "TenantId is required for Exchange Online source")]
    public string TenantId { get; set; } = string.Empty;

    [Required(ErrorMessage = "ClientId is required for Exchange Online source")]
    public string ClientId { get; set; } = string.Empty;

    [Required(ErrorMessage = "ClientSecret is required for Exchange Online source")]
    public string ClientSecret { get; set; } = string.Empty;
}

public class SyncSettings
{
    [Range(1, 1440, ErrorMessage = "SyncIntervalMinutes must be between 1 and 1440")]
    public int SyncIntervalMinutes { get; set; } = 5;

    [Range(1, 365, ErrorMessage = "LookbackDays must be between 1 and 365")]
    public int LookbackDays { get; set; } = 30;

    [Range(1, 365, ErrorMessage = "LookForwardDays must be between 1 and 365")]
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
