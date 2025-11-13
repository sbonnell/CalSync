using ExchangeCalendarSync.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ExchangeCalendarSync.Tests.Logging;

public class InMemoryLoggerProviderTests
{
    [Fact]
    public void CreateLogger_ShouldReturnLogger()
    {
        // Arrange
        var provider = new InMemoryLoggerProvider(100);

        // Act
        var logger = provider.CreateLogger("TestCategory");

        // Assert
        logger.Should().NotBeNull();
    }

    [Fact]
    public void Logger_ShouldStoreLogEntries()
    {
        // Arrange
        var provider = new InMemoryLoggerProvider(100);
        var logger = provider.CreateLogger("TestCategory");

        // Act
        logger.LogInformation("Test message");

        // Assert
        var logs = provider.GetLogs();
        logs.Should().HaveCount(1);
        logs.First().Message.Should().Be("Test message");
        logs.First().LogLevel.Should().Be(LogLevel.Information);
        logs.First().Category.Should().Be("TestCategory");
    }

    [Fact]
    public void Logger_ShouldStoreDifferentLogLevels()
    {
        // Arrange
        var provider = new InMemoryLoggerProvider(100);
        var logger = provider.CreateLogger("TestCategory");

        // Act
        logger.LogDebug("Debug message");
        logger.LogInformation("Info message");
        logger.LogWarning("Warning message");
        logger.LogError("Error message");
        logger.LogCritical("Critical message");

        // Assert
        var logs = provider.GetLogs().ToList();
        logs.Should().HaveCount(5);
        logs[0].LogLevel.Should().Be(LogLevel.Critical);
        logs[1].LogLevel.Should().Be(LogLevel.Error);
        logs[2].LogLevel.Should().Be(LogLevel.Warning);
        logs[3].LogLevel.Should().Be(LogLevel.Information);
        logs[4].LogLevel.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public void GetLogs_WithLevelFilter_ShouldReturnFilteredLogs()
    {
        // Arrange
        var provider = new InMemoryLoggerProvider(100);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("Info message");
        logger.LogWarning("Warning message");
        logger.LogError("Error message");

        // Act
        var errorLogs = provider.GetLogs(LogLevel.Error).ToList();

        // Assert
        errorLogs.Should().HaveCount(1);
        errorLogs[0].LogLevel.Should().Be(LogLevel.Error);
        errorLogs[0].Message.Should().Be("Error message");
    }

    [Fact]
    public void GetLogs_WithLimit_ShouldRespectLimit()
    {
        // Arrange
        var provider = new InMemoryLoggerProvider(100);
        var logger = provider.CreateLogger("TestCategory");

        for (int i = 0; i < 10; i++)
        {
            logger.LogInformation($"Message {i}");
        }

        // Act
        var logs = provider.GetLogs(limit: 5).ToList();

        // Assert
        logs.Should().HaveCount(5);
    }

    [Fact]
    public void Provider_ShouldRespectMaxLogCount()
    {
        // Arrange
        var maxCount = 5;
        var provider = new InMemoryLoggerProvider(maxCount);
        var logger = provider.CreateLogger("TestCategory");

        // Act
        for (int i = 0; i < 10; i++)
        {
            logger.LogInformation($"Message {i}");
        }

        // Assert
        var logs = provider.GetLogs().ToList();
        logs.Should().HaveCount(maxCount);
        // Should keep most recent logs (most-recent-first)
        logs[0].Message.Should().Be("Message 9");
        logs[4].Message.Should().Be("Message 5");
    }

    [Fact]
    public void GetLogs_ShouldReturnMostRecentFirst()
    {
        // Arrange
        var provider = new InMemoryLoggerProvider(100);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("First");
        logger.LogInformation("Second");
        logger.LogInformation("Third");

        // Act
        var logs = provider.GetLogs().ToList();

        // Assert
        logs.Should().HaveCount(3);
        logs[0].Message.Should().Be("Third");
        logs[1].Message.Should().Be("Second");
        logs[2].Message.Should().Be("First");
    }

    [Fact]
    public void Logger_ShouldIncludeTimestamp()
    {
        // Arrange
        var provider = new InMemoryLoggerProvider(100);
        var logger = provider.CreateLogger("TestCategory");
        var beforeLog = DateTime.UtcNow;

        // Act
        logger.LogInformation("Test message");

        // Assert
        var afterLog = DateTime.UtcNow;
        var logs = provider.GetLogs().ToList();
        logs[0].Timestamp.Should().BeAfter(beforeLog.AddSeconds(-1));
        logs[0].Timestamp.Should().BeBefore(afterLog.AddSeconds(1));
    }

    [Fact]
    public void MultipleLoggers_ShouldShareSameStorage()
    {
        // Arrange
        var provider = new InMemoryLoggerProvider(100);
        var logger1 = provider.CreateLogger("Category1");
        var logger2 = provider.CreateLogger("Category2");

        // Act
        logger1.LogInformation("Message from logger1");
        logger2.LogWarning("Message from logger2");

        // Assert
        var logs = provider.GetLogs().ToList();
        logs.Should().HaveCount(2);
        logs.Should().Contain(l => l.Category == "Category1" && l.Message == "Message from logger1");
        logs.Should().Contain(l => l.Category == "Category2" && l.Message == "Message from logger2");
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var provider = new InMemoryLoggerProvider(100);

        // Act
        Action dispose = () => provider.Dispose();

        // Assert
        dispose.Should().NotThrow();
    }
}
