using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;
using ExchangeCalendarSync.Services;
using ExchangeCalendarSync.Logging;
using ExchangeCalendarSync.Middleware;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Exporter;

namespace ExchangeCalendarSync;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configuration - prefer config directory (for Docker volumes), fall back to app directory
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "config", "appsettings.json");
        var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

        if (File.Exists(configPath))
        {
            builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: true);
        }
        else
        {
            builder.Configuration.AddJsonFile(defaultPath, optional: false, reloadOnChange: true);
        }
        builder.Configuration.AddEnvironmentVariables();

        var appSettings = new AppSettings();
        builder.Configuration.Bind(appSettings);

        // Register settings
        builder.Services.AddSingleton(appSettings.ExchangeOnPremise);
        builder.Services.AddSingleton(appSettings.ExchangeOnline);
        if (appSettings.ExchangeOnlineSource != null)
            builder.Services.AddSingleton(appSettings.ExchangeOnlineSource);
        builder.Services.AddSingleton(appSettings.Sync);
        builder.Services.AddSingleton(appSettings.Persistence);
        builder.Services.AddSingleton(appSettings.OpenTelemetry);
        builder.Services.AddSingleton(appSettings);

        // Configure OpenTelemetry metrics if enabled
        if (appSettings.OpenTelemetry.Enabled && appSettings.OpenTelemetry.ExportMetrics)
        {
            ConfigureOpenTelemetryMetrics(builder, appSettings.OpenTelemetry);
        }

        // Register in-memory log provider
        var logProvider = new InMemoryLoggerProvider(maxLogCount: 1000);
        builder.Services.AddSingleton(logProvider);

        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddProvider(logProvider);

        // Add OpenTelemetry logging exporter if enabled
        if (appSettings.OpenTelemetry.Enabled && appSettings.OpenTelemetry.ExportLogs)
        {
            builder.Logging.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                ConfigureOtlpLogExporter(options, appSettings.OpenTelemetry);
            });
        }

        // Register services with interfaces
        builder.Services.AddSingleton<ExchangeOnPremiseService>();
        builder.Services.AddSingleton<ExchangeOnlineSourceService>();
        builder.Services.AddSingleton<ExchangeOnlineService>();
        builder.Services.AddSingleton<ISyncStatusService, SyncStatusService>();
        builder.Services.AddSingleton<ISyncStateRepository, SyncStateRepository>();
        builder.Services.AddSingleton<ICalendarSyncService, CalendarSyncService>();

        // Add background service for sync loop
        builder.Services.AddHostedService<SyncBackgroundService>();

        // Add configuration watcher for auto-restart on settings changes
        builder.Services.AddHostedService<ConfigurationWatcherService>();

        // Add global exception handler
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();

        // Add rate limiting
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Rate limit for sync start endpoint: max 5 requests per minute
            options.AddFixedWindowLimiter("sync", limiterOptions =>
            {
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.PermitLimit = 5;
                limiterOptions.QueueLimit = 0;
            });

            // General API rate limit: max 60 requests per minute
            options.AddFixedWindowLimiter("api", limiterOptions =>
            {
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.PermitLimit = 60;
                limiterOptions.QueueLimit = 2;
            });
        });

        // Add health checks
        builder.Services.AddHealthChecks()
            .AddCheck<SyncServiceHealthCheck>("sync_service");

        // Add controllers and web services
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            });
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
        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseLocalhostOnlyApi(); // Restrict /api/* to localhost only
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();
        app.MapHealthChecks("/health");

        logger.LogInformation("Web interface available at http://localhost:5000");
        logger.LogInformation("Health check available at http://localhost:5000/health");
        logger.LogInformation("Monitoring {Count} mailbox mappings", appSettings.ExchangeOnPremise.GetMailboxMappings().Count);
        logger.LogInformation("Sync interval: {Minutes} minutes", appSettings.Sync.SyncIntervalMinutes);
        logger.LogInformation("State persistence: {Enabled}", appSettings.Persistence.EnableStatePersistence ? "enabled" : "disabled");

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

    static void ConfigureOpenTelemetryMetrics(WebApplicationBuilder builder, OpenTelemetrySettings settings)
    {
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: settings.ServiceName,
                serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0")
            .AddAttributes(new[]
            {
                new KeyValuePair<string, object>("deployment.environment", settings.Environment)
            });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("ExchangeCalendarSync")
                    .AddOtlpExporter(options =>
                    {
                        var isHttp = settings.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase);
                        options.Protocol = isHttp ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc;

                        // For HTTP protocol, append /v1/metrics path; gRPC uses the base endpoint directly
                        var endpoint = settings.Endpoint.TrimEnd('/');
                        options.Endpoint = isHttp
                            ? new Uri($"{endpoint}/v1/metrics")
                            : new Uri(endpoint);

                        if (!string.IsNullOrEmpty(settings.Headers))
                        {
                            options.Headers = settings.Headers;
                        }
                    });
            });
    }

    static void ConfigureOtlpLogExporter(OpenTelemetryLoggerOptions options, OpenTelemetrySettings settings)
    {
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: settings.ServiceName,
                serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0")
            .AddAttributes(new[]
            {
                new KeyValuePair<string, object>("deployment.environment", settings.Environment)
            });

        options.SetResourceBuilder(resourceBuilder);

        options.AddOtlpExporter(exporterOptions =>
        {
            var isHttp = settings.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase);
            exporterOptions.Protocol = isHttp ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc;

            // For HTTP protocol, append /v1/logs path; gRPC uses the base endpoint directly
            var endpoint = settings.Endpoint.TrimEnd('/');
            exporterOptions.Endpoint = isHttp
                ? new Uri($"{endpoint}/v1/logs")
                : new Uri(endpoint);

            if (!string.IsNullOrEmpty(settings.Headers))
            {
                exporterOptions.Headers = settings.Headers;
            }
        });
    }
}
