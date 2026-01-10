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
    private readonly string _settingsFilePath;

    public SettingsController(
        ILogger<SettingsController> logger,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _configuration = configuration;
        _environment = environment;

        // Prefer config directory (for Docker volumes), fall back to app directory
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "config", "appsettings.json");
        var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        _settingsFilePath = System.IO.File.Exists(configPath) ? configPath : defaultPath;
    }

    [HttpGet]
    public IActionResult GetSettings()
    {
        try
        {
            var settings = new SettingsDto
            {
                ExchangeOnPremise = new ExchangeOnPremiseDto
                {
                    ServerUrl = _configuration["ExchangeOnPremise:ServerUrl"] ?? "",
                    Username = _configuration["ExchangeOnPremise:Username"] ?? "",
                    Domain = _configuration["ExchangeOnPremise:Domain"] ?? "",
                    MailboxMappings = _configuration.GetSection("ExchangeOnPremise:MailboxMappings")
                        .Get<List<MailboxMappingDto>>() ?? new List<MailboxMappingDto>()
                },
                ExchangeOnline = new ExchangeOnlineDto
                {
                    TenantId = _configuration["ExchangeOnline:TenantId"] ?? "",
                    ClientId = _configuration["ExchangeOnline:ClientId"] ?? ""
                },
                ExchangeOnlineSource = new ExchangeOnlineSourceDto
                {
                    TenantId = _configuration["ExchangeOnlineSource:TenantId"] ?? "",
                    ClientId = _configuration["ExchangeOnlineSource:ClientId"] ?? ""
                },
                Sync = new SyncDto
                {
                    SyncIntervalMinutes = _configuration.GetValue<int>("Sync:SyncIntervalMinutes", 5),
                    LookbackDays = _configuration.GetValue<int>("Sync:LookbackDays", 30),
                    LookForwardDays = _configuration.GetValue<int>("Sync:LookForwardDays", 30)
                },
                Persistence = new PersistenceDto
                {
                    DataPath = _configuration["Persistence:DataPath"] ?? "./data",
                    EnableStatePersistence = _configuration.GetValue<bool>("Persistence:EnableStatePersistence", true)
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

    [HttpPut]
    public async Task<IActionResult> SaveSettings([FromBody] SettingsDto settings)
    {
        try
        {
            _logger.LogInformation("Saving settings to {Path}", _settingsFilePath);

            // Read existing settings to preserve passwords if not provided
            var existingJson = await System.IO.File.ReadAllTextAsync(_settingsFilePath);
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
            await System.IO.File.WriteAllTextAsync(_settingsFilePath, json);

            _logger.LogInformation("Settings saved successfully. Application will restart to apply changes.");
            return Ok(new { message = "Settings saved successfully. Application will restart automatically to apply changes." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            return StatusCode(500, new { message = "Failed to save settings", error = ex.Message });
        }
    }

    private string GetPasswordOrExisting(string? newPassword, JsonElement existingSettings, string section, string key)
    {
        // If a new password is provided and not empty placeholder, use it
        if (!string.IsNullOrEmpty(newPassword) && newPassword != "********")
        {
            return newPassword;
        }

        // Otherwise, try to get the existing password
        try
        {
            if (existingSettings.TryGetProperty(section, out var sectionElement) &&
                sectionElement.TryGetProperty(key, out var passwordElement))
            {
                return passwordElement.GetString() ?? "";
            }
        }
        catch
        {
            // Ignore errors, return empty string
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
