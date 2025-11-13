using ExchangeCalendarSync.Controllers;
using ExchangeCalendarSync.Logging;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ExchangeCalendarSync.Tests.Controllers;

public class LogsControllerTests
{
    [Fact]
    public void GetLogs_WithNoFilter_ShouldReturnAllLogs()
    {
        // Arrange
        var logProvider = new InMemoryLoggerProvider(100);
        var logger = logProvider.CreateLogger("Test");

        logger.LogInformation("Test message 1");
        logger.LogWarning("Test message 2");
        logger.LogError("Test message 3");

        var controller = new LogsController(logProvider);

        // Act
        var result = controller.GetLogs(limit: 500);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var logs = okResult.Value as IEnumerable<InMemoryLogEntry>;
        logs.Should().NotBeNull().And.HaveCount(3);
    }

    [Fact]
    public void GetLogs_WithLevelFilter_ShouldReturnFilteredLogs()
    {
        // Arrange
        var logProvider = new InMemoryLoggerProvider(100);
        var logger = logProvider.CreateLogger("Test");

        logger.LogInformation("Info message");
        logger.LogWarning("Warning message");
        logger.LogError("Error message");

        var controller = new LogsController(logProvider);

        // Act
        var result = controller.GetLogs(limit: 500, level: "Error");

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var logs = (okResult.Value as IEnumerable<InMemoryLogEntry>)?.ToList();
        logs.Should().NotBeNull().And.HaveCount(1);
        logs![0].LogLevel.Should().Be(LogLevel.Error);
        logs[0].Message.Should().Be("Error message");
    }

    [Fact]
    public void GetLogs_WithLimit_ShouldRespectLimit()
    {
        // Arrange
        var logProvider = new InMemoryLoggerProvider(100);
        var logger = logProvider.CreateLogger("Test");

        for (int i = 0; i < 10; i++)
        {
            logger.LogInformation($"Message {i}");
        }

        var controller = new LogsController(logProvider);

        // Act
        var result = controller.GetLogs(limit: 5);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var logs = okResult.Value as IEnumerable<InMemoryLogEntry>;
        logs.Should().NotBeNull().And.HaveCount(5);
    }

    [Fact]
    public void GetLogs_ShouldReturnMostRecentFirst()
    {
        // Arrange
        var logProvider = new InMemoryLoggerProvider(100);
        var logger = logProvider.CreateLogger("Test");

        logger.LogInformation("First message");
        logger.LogInformation("Second message");
        logger.LogInformation("Third message");

        var controller = new LogsController(logProvider);

        // Act
        var result = controller.GetLogs(limit: 500);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var logs = (okResult.Value as IEnumerable<InMemoryLogEntry>)?.ToList();
        logs.Should().NotBeNull();
        logs![0].Message.Should().Be("Third message");
        logs[2].Message.Should().Be("First message");
    }
}
