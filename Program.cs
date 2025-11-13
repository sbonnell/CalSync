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
        var onlineService = app.Services.GetRequiredService<ExchangeOnlineService>();

        try
        {
            onPremiseService.Initialize();
            onlineService.Initialize();
            logger.LogInformation("Exchange services initialized successfully");
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
        logger.LogInformation("Monitoring {Count} mailboxes", appSettings.ExchangeOnPremise.MailboxesToMonitor.Count);
        logger.LogInformation("Sync interval: {Minutes} minutes", appSettings.Sync.SyncIntervalMinutes);

        await app.RunAsync();
    }

    static void ValidateConfiguration(AppSettings settings, ILogger logger)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(settings.ExchangeOnPremise.ServerUrl))
            errors.Add("ExchangeOnPremise:ServerUrl is required");

        if (string.IsNullOrEmpty(settings.ExchangeOnPremise.Username))
            errors.Add("ExchangeOnPremise:Username is required");

        if (string.IsNullOrEmpty(settings.ExchangeOnPremise.Password))
            errors.Add("ExchangeOnPremise:Password is required");

        if (!settings.ExchangeOnPremise.MailboxesToMonitor.Any())
            errors.Add("ExchangeOnPremise:MailboxesToMonitor must contain at least one mailbox");

        if (string.IsNullOrEmpty(settings.ExchangeOnline.TenantId))
            errors.Add("ExchangeOnline:TenantId is required");

        if (string.IsNullOrEmpty(settings.ExchangeOnline.ClientId))
            errors.Add("ExchangeOnline:ClientId is required");

        if (string.IsNullOrEmpty(settings.ExchangeOnline.ClientSecret))
            errors.Add("ExchangeOnline:ClientSecret is required");

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
    }
}
