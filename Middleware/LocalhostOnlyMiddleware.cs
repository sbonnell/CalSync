using System.Net;

namespace ExchangeCalendarSync.Middleware;

/// <summary>
/// Middleware that restricts access to API endpoints to localhost only.
/// This ensures that the API can only be accessed from the web UI served by the application,
/// while still allowing external access to the health check endpoint.
/// </summary>
public class LocalhostOnlyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LocalhostOnlyMiddleware> _logger;

    public LocalhostOnlyMiddleware(RequestDelegate next, ILogger<LocalhostOnlyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Only apply restriction to /api/* endpoints
        // Allow /health and static files from any source
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            var remoteIp = context.Connection.RemoteIpAddress;

            if (!IsLocalhost(remoteIp))
            {
                _logger.LogWarning("Blocked external API request from {RemoteIp} to {Path}", remoteIp, path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"API access is restricted to localhost only\"}");
                return;
            }
        }

        await _next(context);
    }

    private static bool IsLocalhost(IPAddress? remoteIp)
    {
        if (remoteIp == null)
            return false;

        // Check for IPv4 loopback (127.0.0.1)
        if (IPAddress.IsLoopback(remoteIp))
            return true;

        // Check for IPv6 loopback (::1)
        if (remoteIp.Equals(IPAddress.IPv6Loopback))
            return true;

        // Handle IPv4-mapped IPv6 addresses (::ffff:x.x.x.x)
        if (remoteIp.IsIPv4MappedToIPv6)
        {
            var ipv4 = remoteIp.MapToIPv4();
            if (IPAddress.IsLoopback(ipv4))
                return true;

            // Check for private networks (Docker, local network)
            if (IsPrivateNetwork(ipv4))
                return true;
        }

        // Check for private networks
        if (IsPrivateNetwork(remoteIp))
            return true;

        return false;
    }

    private static bool IsPrivateNetwork(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        // 10.0.0.0/8 - Class A private network
        if (bytes[0] == 10)
            return true;

        // 172.16.0.0/12 - Class B private network (includes Docker's 172.17-31.x.x)
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            return true;

        // 192.168.0.0/16 - Class C private network
        if (bytes[0] == 192 && bytes[1] == 168)
            return true;

        // 127.0.0.0/8 - Loopback
        if (bytes[0] == 127)
            return true;

        return false;
    }
}

/// <summary>
/// Extension methods for adding the LocalhostOnly middleware.
/// </summary>
public static class LocalhostOnlyMiddlewareExtensions
{
    /// <summary>
    /// Adds middleware that restricts /api/* endpoints to localhost only.
    /// The /health endpoint and static files remain accessible externally.
    /// </summary>
    public static IApplicationBuilder UseLocalhostOnlyApi(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<LocalhostOnlyMiddleware>();
    }
}
