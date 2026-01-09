using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;

namespace ExchangeCalendarSync.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);

        var response = new ErrorResponse
        {
            Message = "An error occurred while processing your request.",
            TraceId = httpContext.TraceIdentifier
        };

        // Only include details in development
        if (_environment.IsDevelopment())
        {
            response.Details = exception.Message;
        }

        httpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);

        return true;
    }
}

public class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? TraceId { get; set; }
}
