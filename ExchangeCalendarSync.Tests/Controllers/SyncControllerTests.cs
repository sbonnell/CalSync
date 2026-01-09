using ExchangeCalendarSync.Controllers;
using ExchangeCalendarSync.Models;
using ExchangeCalendarSync.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ExchangeCalendarSync.Tests.Controllers;

public class SyncControllerTests
{
    private readonly Mock<ILogger<SyncController>> _mockLogger;
    private readonly Mock<ICalendarSyncService> _mockSyncService;
    private readonly SyncStatusService _statusService;
    private readonly ExchangeOnPremiseSettings _settings;

    public SyncControllerTests()
    {
        _mockLogger = new Mock<ILogger<SyncController>>();
        _mockSyncService = new Mock<ICalendarSyncService>();
        _statusService = new SyncStatusService();

        _settings = new ExchangeOnPremiseSettings
        {
            MailboxesToMonitor = new List<string> { "test@example.com" }
        };
    }

    [Fact]
    public void GetStatus_ShouldReturnOkWithStatus()
    {
        // Arrange
        var controller = new SyncController(
            _mockLogger.Object,
            _mockSyncService.Object,
            _statusService,
            _settings);

        // Act
        var result = controller.GetStatus();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task StartSync_WhenNotRunning_ShouldStartSync()
    {
        // Arrange
        var controller = new SyncController(
            _mockLogger.Object,
            _mockSyncService.Object,
            _statusService,
            _settings);

        _mockSyncService
            .Setup(s => s.SyncAllMailboxesAsync(It.IsAny<List<MailboxMapping>>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await controller.StartSync();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task StartSync_WhenAlreadyRunning_ShouldReturnBadRequest()
    {
        // Arrange
        var controller = new SyncController(
            _mockLogger.Object,
            _mockSyncService.Object,
            _statusService,
            _settings);

        _statusService.StartSync(); // Mark as running

        // Act
        var result = await controller.StartSync();

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badResult = (BadRequestObjectResult)result;
        badResult.Value.Should().NotBeNull();
        // Extract message from anonymous object { message = "..." }
        var objType = badResult.Value!.GetType();
        var messageProp = objType.GetProperty("message");
        messageProp.Should().NotBeNull();
        var message = messageProp!.GetValue(badResult.Value) as string;
        message.Should().Contain("already running");
    }

    [Fact]
    public async Task StartSync_WhenLockAcquired_ShouldExecuteInBackground()
    {
        // Arrange
        var controller = new SyncController(
            _mockLogger.Object,
            _mockSyncService.Object,
            _statusService,
            _settings);

        var syncExecuted = false;
        _mockSyncService
            .Setup(s => s.SyncAllMailboxesAsync(It.IsAny<List<MailboxMapping>>()))
            .Callback(() => syncExecuted = true)
            .Returns(Task.CompletedTask);

        // Act
        var result = await controller.StartSync();

        // Give background task time to execute
        await Task.Delay(100);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        syncExecuted.Should().BeTrue();
    }
}
