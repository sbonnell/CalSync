namespace ExchangeCalendarSync.Models;

public class AppSettings
{
    public ExchangeOnPremiseSettings ExchangeOnPremise { get; set; } = new();
    public ExchangeOnlineSettings ExchangeOnline { get; set; } = new();
    public SyncSettings Sync { get; set; } = new();
}

public class ExchangeOnPremiseSettings
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public List<string> MailboxesToMonitor { get; set; } = new();
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
