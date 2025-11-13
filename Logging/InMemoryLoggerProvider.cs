using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ExchangeCalendarSync.Logging;

public class InMemoryLogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel LogLevel { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
}

public class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<InMemoryLogEntry> _logs;
    private readonly int _maxLogCount;

    public InMemoryLoggerProvider(int maxLogCount = 1000)
    {
        _logs = new ConcurrentQueue<InMemoryLogEntry>();
        _maxLogCount = maxLogCount;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new InMemoryLogger(categoryName, _logs, _maxLogCount);
    }

    public IEnumerable<InMemoryLogEntry> GetLogs()
    {
        return _logs.ToArray();
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}

public class InMemoryLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ConcurrentQueue<InMemoryLogEntry> _logs;
    private readonly int _maxLogCount;

    public InMemoryLogger(string categoryName, ConcurrentQueue<InMemoryLogEntry> logs, int maxLogCount)
    {
        _categoryName = categoryName;
        _logs = logs;
        _maxLogCount = maxLogCount;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var entry = new InMemoryLogEntry
        {
            Timestamp = DateTime.UtcNow,
            LogLevel = logLevel,
            Category = _categoryName,
            Message = formatter(state, exception),
            Exception = exception?.ToString()
        };

        _logs.Enqueue(entry);

        // Trim old logs if we exceed max count
        while (_logs.Count > _maxLogCount)
        {
            _logs.TryDequeue(out _);
        }
    }
}
