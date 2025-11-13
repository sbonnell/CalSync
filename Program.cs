using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;
using ExchangeCalendarSync.Services;
using ExchangeCalendarSync.Logging;

namespace ExchangeCalendarSync;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configuration
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        builder.Configuration.AddEnvironmentVariables();

        var appSettings = new AppSettings();
        builder.Configuration.Bind(appSettings);

        // Register settings
        builder.Services.AddSingleton(appSettings.ExchangeOnPremise);
        builder.Services.AddSingleton(appSettings.ExchangeOnline);
        builder.Services.AddSingleton(appSettings.ExchangeOnlineSource); // Can be null
        builder.Services.AddSingleton(appSettings.Sync);
        builder.Services.AddSingleton(appSettings);

        // Register in-memory log provider
        var logProvider = new InMemoryLoggerProvider(maxLogCount: 1000);
        builder.Services.AddSingleton(logProvider);

        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddProvider(logProvider);

        // Register services
        builder.Services.AddSingleton<ExchangeOnPremiseService>();
        builder.Services.AddSingleton<ExchangeOnlineSourceService>();
        builder.Services.AddSingleton<ExchangeOnlineService>();
        builder.Services.AddSingleton<SyncStatusService>();
        builder.Services.AddSingleton<CalendarSyncService>();

        // Add background service for sync loop
        builder.Services.AddHostedService<SyncBackgroundService>();

        // Add controllers and web services
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();

        var app = builder.Build();

        // Validate configuration
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        ValidateConfiguration(appSettings, logger);

        // Initialize Exchange services
        logger.LogInformation("Initializing Exchange services...");
        var onPremiseService = app.Services.GetRequiredService<ExchangeOnPremiseService>();
        var onlineSourceService = app.Services.GetRequiredService<ExchangeOnlineSourceService>();
        var onlineService = app.Services.GetRequiredService<ExchangeOnlineService>();

        try
        {
            // Check which source types are being used
            var mappings = appSettings.ExchangeOnPremise.GetMailboxMappings();
            var hasOnPremiseSource = mappings.Any(m => m.SourceType == SourceType.ExchangeOnPremise);
            var hasOnlineSource = mappings.Any(m => m.SourceType == SourceType.ExchangeOnline);

            // Initialize on-premise service if needed
            if (hasOnPremiseSource)
            {
                onPremiseService.Initialize();
                logger.LogInformation("Exchange On-Premise (EWS) service initialized");
            }

            // Initialize online source service if needed
            if (hasOnlineSource)
            {
                onlineSourceService.Initialize();
                logger.LogInformation("Exchange Online Source (Graph) service initialized");
            }

            // Always initialize destination service (Exchange Online)
            onlineService.Initialize();
            logger.LogInformation("Exchange Online Destination service initialized");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize Exchange services");
            throw;
        }

        // Configure HTTP pipeline
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();

        logger.LogInformation("Web interface available at http://localhost:5000");
        logger.LogInformation("Monitoring {Count} mailbox mappings", appSettings.ExchangeOnPremise.GetMailboxMappings().Count);
        logger.LogInformation("Sync interval: {Minutes} minutes", appSettings.Sync.SyncIntervalMinutes);

        await app.RunAsync();
    }

    static void ValidateConfiguration(AppSettings settings, ILogger logger)
    {
        var errors = new List<string>();
        var mappings = settings.ExchangeOnPremise.GetMailboxMappings();

        // Check if any mailboxes are configured
        if (!mappings.Any())
            errors.Add("No mailboxes configured. Add mailboxes to MailboxesToMonitor or MailboxMappings");

        // Check which source types are being used
        var hasOnPremiseSource = mappings.Any(m => m.SourceType == SourceType.ExchangeOnPremise);
        var hasOnlineSource = mappings.Any(m => m.SourceType == SourceType.ExchangeOnline);

        // Validate Exchange On-Premise settings if used as source
        if (hasOnPremiseSource)
        {
            if (string.IsNullOrEmpty(settings.ExchangeOnPremise.ServerUrl))
                errors.Add("ExchangeOnPremise:ServerUrl is required when using Exchange On-Premise as source");

            if (string.IsNullOrEmpty(settings.ExchangeOnPremise.Username))
                errors.Add("ExchangeOnPremise:Username is required when using Exchange On-Premise as source");

            if (string.IsNullOrEmpty(settings.ExchangeOnPremise.Password))
                errors.Add("ExchangeOnPremise:Password is required when using Exchange On-Premise as source");
        }

        // Validate Exchange Online Source settings if used as source
        if (hasOnlineSource)
        {
            if (settings.ExchangeOnlineSource == null)
                errors.Add("ExchangeOnlineSource configuration is required when using Exchange Online as source");
            else
            {
                if (string.IsNullOrEmpty(settings.ExchangeOnlineSource.TenantId))
                    errors.Add("ExchangeOnlineSource:TenantId is required when using Exchange Online as source");

                if (string.IsNullOrEmpty(settings.ExchangeOnlineSource.ClientId))
                    errors.Add("ExchangeOnlineSource:ClientId is required when using Exchange Online as source");

                if (string.IsNullOrEmpty(settings.ExchangeOnlineSource.ClientSecret))
                    errors.Add("ExchangeOnlineSource:ClientSecret is required when using Exchange Online as source");
            }
        }

        // Validate Exchange Online (destination) settings - always required
        if (string.IsNullOrEmpty(settings.ExchangeOnline.TenantId))
            errors.Add("ExchangeOnline:TenantId is required for destination");

        if (string.IsNullOrEmpty(settings.ExchangeOnline.ClientId))
            errors.Add("ExchangeOnline:ClientId is required for destination");

        if (string.IsNullOrEmpty(settings.ExchangeOnline.ClientSecret))
            errors.Add("ExchangeOnline:ClientSecret is required for destination");

        if (errors.Any())
        {
            logger.LogError("Configuration validation failed:");
            foreach (var error in errors)
            {
                logger.LogError("  - {Error}", error);
            }
            throw new InvalidOperationException("Invalid configuration. Please check appsettings.json");
        }

        logger.LogInformation("Configuration validation passed");
        logger.LogInformation("Source types configured: {Sources}",
            string.Join(", ", new[]
            {
                hasOnPremiseSource ? "Exchange On-Premise (EWS)" : null,
                hasOnlineSource ? "Exchange Online (Graph)" : null
            }.Where(s => s != null)));
    }
}
