using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ExchangeCalendarSync.Models;
using System.Text.Json;

namespace ExchangeCalendarSync.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("api")]
public class SettingsController : ControllerBase
{
    private readonly ILogger<SettingsController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public SettingsController(
        ILogger<SettingsController> logger,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
    }

    /// <summary>
    /// Gets the settings file path, checking on each call to handle Docker volume mounts
    /// </summary>
    private string GetSettingsFilePath()
    {
        // Prefer config directory (for Docker volumes), fall back to app directory
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "config", "appsettings.json");
        var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        return System.IO.File.Exists(configPath) ? configPath : defaultPath;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        try
        {
            // Read directly from file to ensure we get the latest values
            // (IConfiguration may have stale data before app restart completes)
            var settingsPath = GetSettingsFilePath();
            var json = await System.IO.File.ReadAllTextAsync(settingsPath);
            var fileSettings = JsonSerializer.Deserialize<JsonElement>(json);

            var settings = new SettingsDto
            {
                ExchangeOnPremise = new ExchangeOnPremiseDto
                {
                    ServerUrl = GetJsonString(fileSettings, "ExchangeOnPremise", "ServerUrl"),
                    Username = GetJsonString(fileSettings, "ExchangeOnPremise", "Username"),
                    Domain = GetJsonString(fileSettings, "ExchangeOnPremise", "Domain"),
                    MailboxMappings = GetMailboxMappings(fileSettings)
                },
                ExchangeOnline = new ExchangeOnlineDto
                {
                    TenantId = GetJsonString(fileSettings, "ExchangeOnline", "TenantId"),
                    ClientId = GetJsonString(fileSettings, "ExchangeOnline", "ClientId")
                },
                ExchangeOnlineSource = new ExchangeOnlineSourceDto
                {
                    TenantId = GetJsonString(fileSettings, "ExchangeOnlineSource", "TenantId"),
                    ClientId = GetJsonString(fileSettings, "ExchangeOnlineSource", "ClientId")
                },
                Sync = new SyncDto
                {
                    SyncIntervalMinutes = GetJsonInt(fileSettings, "Sync", "SyncIntervalMinutes", 5),
                    LookbackDays = GetJsonInt(fileSettings, "Sync", "LookbackDays", 30),
                    LookForwardDays = GetJsonInt(fileSettings, "Sync", "LookForwardDays", 30)
                },
                Persistence = new PersistenceDto
                {
                    DataPath = GetJsonString(fileSettings, "Persistence", "DataPath", "./data"),
                    EnableStatePersistence = GetJsonBool(fileSettings, "Persistence", "EnableStatePersistence", true)
                },
                OpenTelemetry = new OpenTelemetryDto
                {
                    Enabled = GetJsonBool(fileSettings, "OpenTelemetry", "Enabled", false),
                    Endpoint = GetJsonString(fileSettings, "OpenTelemetry", "Endpoint", "http://localhost:4317"),
                    ServiceName = GetJsonString(fileSettings, "OpenTelemetry", "ServiceName", "exchange-calendar-sync"),
                    Environment = GetJsonString(fileSettings, "OpenTelemetry", "Environment", "production"),
                    ExportLogs = GetJsonBool(fileSettings, "OpenTelemetry", "ExportLogs", true),
                    ExportMetrics = GetJsonBool(fileSettings, "OpenTelemetry", "ExportMetrics", true),
                    Protocol = GetJsonString(fileSettings, "OpenTelemetry", "Protocol", "grpc"),
                    // Return actual headers value (not masked) so user can see/edit it
                    Headers = GetJsonString(fileSettings, "OpenTelemetry", "Headers"),
                    MetricsExportIntervalSeconds = GetJsonInt(fileSettings, "OpenTelemetry", "MetricsExportIntervalSeconds", 60)
                }
            };

            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get settings");
            return StatusCode(500, new { message = "Failed to get settings", error = ex.Message });
        }
    }

    private string GetJsonString(JsonElement root, string section, string key, string defaultValue = "")
    {
        try
        {
            if (root.TryGetProperty(section, out var sectionElement) &&
                sectionElement.TryGetProperty(key, out var valueElement) &&
                valueElement.ValueKind == JsonValueKind.String)
            {
                return valueElement.GetString() ?? defaultValue;
            }
        }
        catch { }
        return defaultValue;
    }

    private int GetJsonInt(JsonElement root, string section, string key, int defaultValue)
    {
        try
        {
            if (root.TryGetProperty(section, out var sectionElement) &&
                sectionElement.TryGetProperty(key, out var valueElement) &&
                valueElement.ValueKind == JsonValueKind.Number)
            {
                return valueElement.GetInt32();
            }
        }
        catch { }
        return defaultValue;
    }

    private bool GetJsonBool(JsonElement root, string section, string key, bool defaultValue)
    {
        try
        {
            if (root.TryGetProperty(section, out var sectionElement) &&
                sectionElement.TryGetProperty(key, out var valueElement) &&
                (valueElement.ValueKind == JsonValueKind.True || valueElement.ValueKind == JsonValueKind.False))
            {
                return valueElement.GetBoolean();
            }
        }
        catch { }
        return defaultValue;
    }

    private List<MailboxMappingDto> GetMailboxMappings(JsonElement root)
    {
        var mappings = new List<MailboxMappingDto>();
        try
        {
            if (root.TryGetProperty("ExchangeOnPremise", out var section) &&
                section.TryGetProperty("MailboxMappings", out var mappingsElement) &&
                mappingsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in mappingsElement.EnumerateArray())
                {
                    mappings.Add(new MailboxMappingDto
                    {
                        Name = item.TryGetProperty("Name", out var n) ? n.GetString() : "",
                        SourceMailbox = item.TryGetProperty("SourceMailbox", out var s) ? s.GetString() : "",
                        DestinationMailbox = item.TryGetProperty("DestinationMailbox", out var d) ? d.GetString() : "",
                        SourceType = item.TryGetProperty("SourceType", out var t) ? t.GetString() : "ExchangeOnPremise"
                    });
                }
            }
        }
        catch { }
        return mappings;
    }

    [HttpPut]
    public async Task<IActionResult> SaveSettings([FromBody] SettingsDto settings)
    {
        try
        {
            var settingsPath = GetSettingsFilePath();

            // Read existing settings to preserve passwords if not provided
            var existingJson = await System.IO.File.ReadAllTextAsync(settingsPath);
            var existingSettings = JsonSerializer.Deserialize<JsonElement>(existingJson);

            var newSettings = new Dictionary<string, object>
            {
                ["ExchangeOnPremise"] = new Dictionary<string, object>
                {
                    ["ServerUrl"] = settings.ExchangeOnPremise?.ServerUrl ?? "",
                    ["Username"] = settings.ExchangeOnPremise?.Username ?? "",
                    ["Password"] = GetPasswordOrExisting(settings.ExchangeOnPremise?.Password, existingSettings, "ExchangeOnPremise", "Password"),
                    ["Domain"] = settings.ExchangeOnPremise?.Domain ?? "",
                    ["MailboxesToMonitor"] = new List<string>(),
                    ["MailboxMappings"] = settings.ExchangeOnPremise?.MailboxMappings?.Select(m => new Dictionary<string, object>
                    {
                        ["Name"] = m.Name ?? "",
                        ["SourceMailbox"] = m.SourceMailbox ?? "",
                        ["DestinationMailbox"] = m.DestinationMailbox ?? "",
                        ["SourceType"] = m.SourceType ?? "ExchangeOnPremise"
                    }).ToList() ?? new List<Dictionary<string, object>>()
                },
                ["ExchangeOnline"] = new Dictionary<string, object>
                {
                    ["TenantId"] = settings.ExchangeOnline?.TenantId ?? "",
                    ["ClientId"] = settings.ExchangeOnline?.ClientId ?? "",
                    ["ClientSecret"] = GetPasswordOrExisting(settings.ExchangeOnline?.ClientSecret, existingSettings, "ExchangeOnline", "ClientSecret")
                },
                ["ExchangeOnlineSource"] = new Dictionary<string, object>
                {
                    ["TenantId"] = settings.ExchangeOnlineSource?.TenantId ?? "",
                    ["ClientId"] = settings.ExchangeOnlineSource?.ClientId ?? "",
                    ["ClientSecret"] = GetPasswordOrExisting(settings.ExchangeOnlineSource?.ClientSecret, existingSettings, "ExchangeOnlineSource", "ClientSecret")
                },
                ["Sync"] = new Dictionary<string, object>
                {
                    ["SyncIntervalMinutes"] = settings.Sync?.SyncIntervalMinutes ?? 5,
                    ["LookbackDays"] = settings.Sync?.LookbackDays ?? 30,
                    ["LookForwardDays"] = settings.Sync?.LookForwardDays ?? 30
                },
                ["Persistence"] = new Dictionary<string, object>
                {
                    ["DataPath"] = settings.Persistence?.DataPath ?? "./data",
                    ["EnableStatePersistence"] = settings.Persistence?.EnableStatePersistence ?? true
                },
                ["OpenTelemetry"] = new Dictionary<string, object>
                {
                    ["Enabled"] = settings.OpenTelemetry?.Enabled ?? false,
                    ["Endpoint"] = settings.OpenTelemetry?.Endpoint ?? "http://localhost:4317",
                    ["ServiceName"] = settings.OpenTelemetry?.ServiceName ?? "exchange-calendar-sync",
                    ["Environment"] = settings.OpenTelemetry?.Environment ?? "production",
                    ["ExportLogs"] = settings.OpenTelemetry?.ExportLogs ?? true,
                    ["ExportMetrics"] = settings.OpenTelemetry?.ExportMetrics ?? true,
                    ["Protocol"] = settings.OpenTelemetry?.Protocol ?? "grpc",
                    ["Headers"] = GetPasswordOrExisting(settings.OpenTelemetry?.Headers, existingSettings, "OpenTelemetry", "Headers"),
                    ["MetricsExportIntervalSeconds"] = settings.OpenTelemetry?.MetricsExportIntervalSeconds ?? 60
                },
                ["Logging"] = new Dictionary<string, object>
                {
                    ["LogLevel"] = new Dictionary<string, object>
                    {
                        ["Default"] = "Information",
                        ["Microsoft"] = "Warning"
                    }
                }
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(newSettings, options);
            await System.IO.File.WriteAllTextAsync(settingsPath, json);

            _logger.LogInformation("Settings saved successfully");
            return Ok(new { message = "Settings saved successfully. Application will restart automatically to apply changes." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            return StatusCode(500, new { message = "Failed to save settings", error = ex.Message });
        }
    }

    private string GetPasswordOrExisting(string? newValue, JsonElement existingSettings, string section, string key)
    {
        // If a new value is provided (not null, not empty, and not the mask placeholder), use it
        if (!string.IsNullOrWhiteSpace(newValue) && newValue != "********")
        {
            return newValue;
        }

        // Otherwise, try to get the existing value from the config file
        try
        {
            if (existingSettings.TryGetProperty(section, out var sectionElement) &&
                sectionElement.TryGetProperty(key, out var valueElement) &&
                valueElement.ValueKind == JsonValueKind.String)
            {
                return valueElement.GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading existing value for {Section}:{Key}", section, key);
        }

        return "";
    }
}

// DTOs for settings API (exclude sensitive data on read, accept on write)
public class SettingsDto
{
    public ExchangeOnPremiseDto? ExchangeOnPremise { get; set; }
    public ExchangeOnlineDto? ExchangeOnline { get; set; }
    public ExchangeOnlineSourceDto? ExchangeOnlineSource { get; set; }
    public SyncDto? Sync { get; set; }
    public PersistenceDto? Persistence { get; set; }
    public OpenTelemetryDto? OpenTelemetry { get; set; }
}

public class ExchangeOnPremiseDto
{
    public string? ServerUrl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Domain { get; set; }
    public List<MailboxMappingDto>? MailboxMappings { get; set; }
}

public class ExchangeOnlineDto
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public class ExchangeOnlineSourceDto
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public class SyncDto
{
    public int SyncIntervalMinutes { get; set; }
    public int LookbackDays { get; set; }
    public int LookForwardDays { get; set; }
}

public class PersistenceDto
{
    public string? DataPath { get; set; }
    public bool EnableStatePersistence { get; set; }
}

public class MailboxMappingDto
{
    public string? Name { get; set; }
    public string? SourceMailbox { get; set; }
    public string? DestinationMailbox { get; set; }
    public string? SourceType { get; set; }
}

public class OpenTelemetryDto
{
    public bool Enabled { get; set; }
    public string? Endpoint { get; set; }
    public string? ServiceName { get; set; }
    public string? Environment { get; set; }
    public bool ExportLogs { get; set; }
    public bool ExportMetrics { get; set; }
    public string? Protocol { get; set; }
    public string? Headers { get; set; }
    public int MetricsExportIntervalSeconds { get; set; }
}
