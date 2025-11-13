using Microsoft.AspNetCore.Mvc;
using ExchangeCalendarSync.Logging;

namespace ExchangeCalendarSync.Controllers;

[ApiController]
[Route("api/[controller]")]
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

        // Filter by log level if specified
        if (!string.IsNullOrEmpty(level))
        {
            if (Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(level, true, out var logLevel))
            {
                logs = logs.Where(l => l.LogLevel == logLevel);
            }
        }

        // Take the most recent logs
        var result = logs.OrderByDescending(l => l.Timestamp).Take(limit).ToList();

        return Ok(result);
    }
}
