using System.Text.Json;
using Microsoft.Extensions.Logging;
using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Services;

public interface ISyncStateRepository
{
    Task<PersistedSyncState> LoadStateAsync();
    Task SaveStateAsync(PersistedSyncState state);
}

public class SyncStateRepository : ISyncStateRepository
{
    private readonly ILogger<SyncStateRepository> _logger;
    private readonly PersistenceSettings _settings;
    private readonly string _stateFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public SyncStateRepository(
        ILogger<SyncStateRepository> logger,
        PersistenceSettings settings)
    {
        _logger = logger;
        _settings = settings;

        // Ensure data directory exists
        if (!Directory.Exists(_settings.DataPath))
        {
            Directory.CreateDirectory(_settings.DataPath);
        }

        _stateFilePath = Path.Combine(_settings.DataPath, "sync-state.json");
    }

    public async Task<PersistedSyncState> LoadStateAsync()
    {
        if (!_settings.EnableStatePersistence)
        {
            return new PersistedSyncState();
        }

        await _fileLock.WaitAsync();
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                _logger.LogInformation("No existing state file found at {Path}", _stateFilePath);
                return new PersistedSyncState();
            }

            var json = await File.ReadAllTextAsync(_stateFilePath);
            var state = JsonSerializer.Deserialize<PersistedSyncState>(json);

            if (state != null)
            {
                _logger.LogInformation("Loaded sync state from {Path}, last persisted at {Time}",
                    _stateFilePath, state.LastPersistedAt);
                return state;
            }

            return new PersistedSyncState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load state from {Path}, starting fresh", _stateFilePath);
            return new PersistedSyncState();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveStateAsync(PersistedSyncState state)
    {
        if (!_settings.EnableStatePersistence)
        {
            return;
        }

        await _fileLock.WaitAsync();
        try
        {
            state.LastPersistedAt = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Write to temp file first, then rename for atomic operation
            var tempPath = _stateFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, _stateFilePath, overwrite: true);

            _logger.LogDebug("Persisted sync state to {Path}", _stateFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state to {Path}", _stateFilePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
