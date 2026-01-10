using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ExchangeCalendarSync.Services;
using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("api")]
public class SyncController : ControllerBase
{
    private readonly ILogger<SyncController> _logger;
    private readonly ICalendarSyncService _syncService;
    private readonly ISyncStatusService _statusService;
    private readonly ExchangeOnPremiseSettings _settings;
    private readonly IHostApplicationLifetime _applicationLifetime;

    public SyncController(
        ILogger<SyncController> logger,
        ICalendarSyncService syncService,
        ISyncStatusService statusService,
        ExchangeOnPremiseSettings settings,
        IHostApplicationLifetime applicationLifetime)
    {
        _logger = logger;
        _syncService = syncService;
        _statusService = statusService;
        _settings = settings;
        _applicationLifetime = applicationLifetime;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var status = _statusService.GetStatus();
        return Ok(status);
    }

    [HttpPost("start")]
    [EnableRateLimiting("sync")]
    public async Task<IActionResult> StartSync([FromQuery] bool fullSync = false)
    {
        var syncType = fullSync ? "full" : "incremental";
        _logger.LogInformation("Manual {SyncType} sync requested via API", syncType);

        // Check if a sync is already running
        if (_statusService.IsRunning())
        {
            return BadRequest(new { message = "Sync is already running" });
        }

        // Try to acquire the sync lock
        if (!await _statusService.TryAcquireSyncLock())
        {
            return BadRequest(new { message = "Could not acquire sync lock" });
        }

        try
        {
            // Start sync in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await _syncService.SyncAllMailboxesAsync(_settings.GetMailboxMappings(), fullSync);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Manual {SyncType} sync failed", syncType);
                }
                finally
                {
                    _statusService.ReleaseSyncLock();
                }
            });

            return Ok(new { message = $"{(fullSync ? "Full" : "Incremental")} sync started successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start manual {SyncType} sync", syncType);
            _statusService.ReleaseSyncLock();
            return StatusCode(500, new { message = "Failed to start sync", error = ex.Message });
        }
    }

    [HttpPost("toggle")]
    public IActionResult ToggleSync()
    {
        var currentState = _statusService.IsSyncEnabled();
        var newState = !currentState;
        _statusService.SetSyncEnabled(newState);
        _logger.LogInformation("Sync scheduler {State} via API", newState ? "enabled" : "disabled");
        return Ok(new { enabled = newState, message = newState ? "Sync scheduler enabled" : "Sync scheduler disabled" });
    }

    [HttpPost("restart")]
    public IActionResult RestartApplication()
    {
        _logger.LogInformation("Application restart requested via API");

        // Schedule the shutdown after a short delay to allow the response to be sent
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            _applicationLifetime.StopApplication();
        });

        return Ok(new { message = "Application is restarting..." });
    }
}
