using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExchangeCalendarSync.Services;

/// <summary>
/// Background service that watches for configuration file changes and triggers application restart.
/// </summary>
public class ConfigurationWatcherService : BackgroundService
{
    private readonly ILogger<ConfigurationWatcherService> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly string _configFilePath;
    private FileSystemWatcher? _fileWatcher;
    private DateTime _lastRestartTrigger = DateTime.MinValue;
    private readonly TimeSpan _debounceInterval = TimeSpan.FromSeconds(2);

    public ConfigurationWatcherService(
        ILogger<ConfigurationWatcherService> logger,
        IHostApplicationLifetime applicationLifetime)
    {
        _logger = logger;
        _applicationLifetime = applicationLifetime;

        // Determine which config file to watch
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "config", "appsettings.json");
        var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        _configFilePath = File.Exists(configPath) ? configPath : defaultPath;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Configuration watcher started, monitoring: {Path}", _configFilePath);

        var directory = Path.GetDirectoryName(_configFilePath);
        var fileName = Path.GetFileName(_configFilePath);

        if (string.IsNullOrEmpty(directory))
        {
            _logger.LogWarning("Could not determine config directory, configuration watching disabled");
            return Task.CompletedTask;
        }

        _fileWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _fileWatcher.Changed += OnConfigurationChanged;
        _fileWatcher.Created += OnConfigurationChanged;

        stoppingToken.Register(() =>
        {
            _fileWatcher?.Dispose();
            _logger.LogInformation("Configuration watcher stopped");
        });

        return Task.CompletedTask;
    }

    private void OnConfigurationChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce to prevent multiple restarts from rapid file changes
        var now = DateTime.UtcNow;
        if (now - _lastRestartTrigger < _debounceInterval)
        {
            return;
        }
        _lastRestartTrigger = now;

        _logger.LogInformation("Configuration file changed: {ChangeType}. Application will restart to apply changes.", e.ChangeType);

        // Give a brief moment for file writes to complete
        Task.Delay(500).ContinueWith(_ =>
        {
            _logger.LogInformation("Initiating application restart...");
            _applicationLifetime.StopApplication();
        });
    }

    public override void Dispose()
    {
        _fileWatcher?.Dispose();
        base.Dispose();
    }
}
