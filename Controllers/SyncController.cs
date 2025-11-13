using Microsoft.AspNetCore.Mvc;
using ExchangeCalendarSync.Services;
using ExchangeCalendarSync.Models;

namespace ExchangeCalendarSync.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly ILogger<SyncController> _logger;
    private readonly CalendarSyncService _syncService;
    private readonly SyncStatusService _statusService;
    private readonly ExchangeOnPremiseSettings _settings;

    public SyncController(
        ILogger<SyncController> logger,
        CalendarSyncService syncService,
        SyncStatusService statusService,
        ExchangeOnPremiseSettings settings)
    {
        _logger = logger;
        _syncService = syncService;
        _statusService = statusService;
        _settings = settings;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var status = _statusService.GetStatus();
        return Ok(status);
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartSync()
    {
        _logger.LogInformation("Manual sync requested via API");

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
                    await _syncService.SyncAllMailboxesAsync(_settings.MailboxesToMonitor);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Manual sync failed");
                }
                finally
                {
                    _statusService.ReleaseSyncLock();
                }
            });

            return Ok(new { message = "Sync started successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start manual sync");
            _statusService.ReleaseSyncLock();
            return StatusCode(500, new { message = "Failed to start sync", error = ex.Message });
        }
    }
}
