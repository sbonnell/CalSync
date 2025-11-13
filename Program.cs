using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;
using ExchangeCalendarSync.Services;

namespace ExchangeCalendarSync;

class Program
{
    static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Exchange Calendar Sync Service starting...");

        try
        {
            var config = host.Services.GetRequiredService<IConfiguration>();
            var appSettings = new AppSettings();
            config.Bind(appSettings);

            // Validate configuration
            ValidateConfiguration(appSettings, logger);

            // Initialize services
            var onPremiseService = host.Services.GetRequiredService<ExchangeOnPremiseService>();
            var onlineService = host.Services.GetRequiredService<ExchangeOnlineService>();
            var syncService = host.Services.GetRequiredService<CalendarSyncService>();

            logger.LogInformation("Initializing Exchange services...");
            onPremiseService.Initialize();
            onlineService.Initialize();

            logger.LogInformation("Starting calendar sync loop...");
            logger.LogInformation("Monitoring {Count} mailboxes", appSettings.ExchangeOnPremise.MailboxesToMonitor.Count);
            logger.LogInformation("Sync interval: {Minutes} minutes", appSettings.Sync.SyncIntervalMinutes);

            // Main sync loop
            while (true)
            {
                try
                {
                    await syncService.SyncAllMailboxesAsync(appSettings.ExchangeOnPremise.MailboxesToMonitor);

                    logger.LogInformation("Next sync in {Minutes} minutes", appSettings.Sync.SyncIntervalMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(appSettings.Sync.SyncIntervalMinutes));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during sync operation");
                    logger.LogInformation("Retrying in 1 minute...");
                    await Task.Delay(TimeSpan.FromMinutes(1));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Fatal error occurred. Service is shutting down.");
            throw;
        }
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                var config = context.Configuration;

                // Register settings
                var appSettings = new AppSettings();
                config.Bind(appSettings);

                services.AddSingleton(appSettings.ExchangeOnPremise);
                services.AddSingleton(appSettings.ExchangeOnline);
                services.AddSingleton(appSettings.Sync);

                // Register services
                services.AddSingleton<ExchangeOnPremiseService>();
                services.AddSingleton<ExchangeOnlineService>();
                services.AddSingleton<CalendarSyncService>();

                // Configure logging
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.AddConfiguration(config.GetSection("Logging"));
                });
            });

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
