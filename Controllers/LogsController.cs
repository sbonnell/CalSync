using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ExchangeCalendarSync.Logging;

namespace ExchangeCalendarSync.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("api")]
public class LogsController : ControllerBase
{
    private readonly InMemoryLoggerProvider _logProvider;

    public LogsController(InMemoryLoggerProvider logProvider)
    {
        _logProvider = logProvider;
    }

    [HttpGet]
    public IActionResult GetLogs([FromQuery] int limit = 500, [FromQuery] string? level = null)
    {
        var logs = _logProvider.GetLogs();

        // Filter by minimum log level if specified (includes selected level and more severe)
        if (!string.IsNullOrEmpty(level))
        {
            if (Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(level, true, out var minLevel))
            {
                logs = logs.Where(l => l.LogLevel >= minLevel);
            }
        }

        // Take the most recent logs
        var result = logs.OrderByDescending(l => l.Timestamp).Take(limit).ToList();

        return Ok(result);
    }
}
