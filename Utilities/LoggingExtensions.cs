namespace ExchangeCalendarSync.Utilities;

/// <summary>
/// Extension methods for logging utilities.
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Converts an optional mapping name to a log prefix format.
    /// </summary>
    /// <param name="mappingName">The mapping name, or null/empty for no prefix.</param>
    /// <returns>A formatted log prefix like "[MappingName] " or empty string.</returns>
    public static string ToLogPrefix(this string? mappingName)
        => !string.IsNullOrEmpty(mappingName) ? $"[{mappingName}] " : "";
}
