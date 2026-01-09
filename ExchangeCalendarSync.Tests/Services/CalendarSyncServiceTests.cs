using ExchangeCalendarSync.Models;
using ExchangeCalendarSync.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ExchangeCalendarSync.Tests.Services;

public class CalendarSyncServiceTests
{
    private readonly Mock<ILogger<CalendarSyncService>> _mockLogger;
    private readonly Mock<ExchangeOnPremiseService> _mockOnPremiseService;
    private readonly Mock<ExchangeOnlineSourceService> _mockOnlineSourceService;
    private readonly Mock<ExchangeOnlineService> _mockOnlineService;
    private readonly Mock<ISyncStateRepository> _mockStateRepository;
    private readonly SyncStatusService _statusService;
    private readonly SyncSettings _syncSettings;

    public CalendarSyncServiceTests()
    {
        _mockLogger = new Mock<ILogger<CalendarSyncService>>();

        var mockOnPremLogger = new Mock<ILogger<ExchangeOnPremiseService>>();
        var onPremSettings = new ExchangeOnPremiseSettings();
        _mockOnPremiseService = new Mock<ExchangeOnPremiseService>(mockOnPremLogger.Object, onPremSettings);

        var mockOnlineSourceLogger = new Mock<ILogger<ExchangeOnlineSourceService>>();
        _mockOnlineSourceService = new Mock<ExchangeOnlineSourceService>(mockOnlineSourceLogger.Object, null);

        var mockOnlineLogger = new Mock<ILogger<ExchangeOnlineService>>();
        var onlineSettings = new ExchangeOnlineSettings();
        _mockOnlineService = new Mock<ExchangeOnlineService>(mockOnlineLogger.Object, onlineSettings);

        _mockStateRepository = new Mock<ISyncStateRepository>();
        _mockStateRepository.Setup(r => r.LoadStateAsync()).ReturnsAsync(new PersistedSyncState());
        _mockStateRepository.Setup(r => r.SaveStateAsync(It.IsAny<PersistedSyncState>())).Returns(Task.CompletedTask);

        _statusService = new SyncStatusService();
        _syncSettings = new SyncSettings
        {
            SyncIntervalMinutes = 5,
            LookbackDays = 30
        };
    }

    [Fact]
    public async Task SyncAllMailboxesAsync_WithLegacyFormat_ShouldConvertToMappings()
    {
        // Arrange
        var service = new CalendarSyncService(
            _mockLogger.Object,
            _mockOnPremiseService.Object,
            _mockOnlineSourceService.Object,
            _mockOnlineService.Object,
            _syncSettings,
            _statusService,
            _mockStateRepository.Object
        );

        var mailboxes = new List<string> { "test@example.com" };

        _mockOnPremiseService
            .Setup(s => s.GetCalendarItemsAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarItemSync>());

        // Act
        await service.SyncAllMailboxesAsync(mailboxes);

        // Assert
        _mockOnPremiseService.Verify(
            s => s.GetCalendarItemsAsync("test@example.com", It.IsAny<DateTime>(), It.IsAny<DateTime>()),
            Times.Once
        );
    }

    [Fact]
    public async Task SyncAllMailboxesAsync_WithEmptyCalendar_ShouldNotAttemptSync()
    {
        // Arrange
        var service = new CalendarSyncService(
            _mockLogger.Object,
            _mockOnPremiseService.Object,
            _mockOnlineSourceService.Object,
            _mockOnlineService.Object,
            _syncSettings,
            _statusService,
            _mockStateRepository.Object
        );

        var mappings = new List<MailboxMapping>
        {
            new()
            {
                SourceMailbox = "test@example.com",
                DestinationMailbox = "test@cloud.com",
                SourceType = SourceType.ExchangeOnPremise
            }
        };

        _mockOnPremiseService
            .Setup(s => s.GetCalendarItemsAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarItemSync>());

        // Act
        await service.SyncAllMailboxesAsync(mappings);

        // Assert
        _mockOnlineService.Verify(
            s => s.SyncCalendarItemAsync(It.IsAny<CalendarItemSync>()),
            Times.Never
        );
    }

    [Fact]
    public async Task SyncAllMailboxesAsync_WithCalendarItems_ShouldSyncToDestination()
    {
        // Arrange
        var service = new CalendarSyncService(
            _mockLogger.Object,
            _mockOnPremiseService.Object,
            _mockOnlineSourceService.Object,
            _mockOnlineService.Object,
            _syncSettings,
            _statusService,
            _mockStateRepository.Object
        );

        var mappings = new List<MailboxMapping>
        {
            new()
            {
                SourceMailbox = "test@example.com",
                DestinationMailbox = "test@cloud.com",
                SourceType = SourceType.ExchangeOnPremise
            }
        };

        var calendarItems = new List<CalendarItemSync>
        {
            new()
            {
                Id = "1",
                Subject = "Test Meeting",
                SourceMailbox = "test@example.com"
            }
        };

        _mockOnPremiseService
            .Setup(s => s.GetCalendarItemsAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(calendarItems);

        _mockOnlineService
            .Setup(s => s.SyncCalendarItemAsync(It.IsAny<CalendarItemSync>()))
            .ReturnsAsync(true);

        // Act
        await service.SyncAllMailboxesAsync(mappings);

        // Assert
        _mockOnlineService.Verify(
            s => s.SyncCalendarItemAsync(It.Is<CalendarItemSync>(item =>
                item.DestinationMailbox == "test@cloud.com"
            )),
            Times.Once
        );
    }

    [Fact]
    public async Task SyncAllMailboxesAsync_WithExchangeOnlineSource_ShouldUseGraphAPI()
    {
        // Arrange
        var service = new CalendarSyncService(
            _mockLogger.Object,
            _mockOnPremiseService.Object,
            _mockOnlineSourceService.Object,
            _mockOnlineService.Object,
            _syncSettings,
            _statusService,
            _mockStateRepository.Object
        );

        var mappings = new List<MailboxMapping>
        {
            new()
            {
                SourceMailbox = "test@source.com",
                DestinationMailbox = "test@dest.com",
                SourceType = SourceType.ExchangeOnline
            }
        };

        _mockOnlineSourceService
            .Setup(s => s.GetCalendarItemsAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarItemSync>());

        // Act
        await service.SyncAllMailboxesAsync(mappings);

        // Assert
        _mockOnlineSourceService.Verify(
            s => s.GetCalendarItemsAsync("test@source.com", It.IsAny<DateTime>(), It.IsAny<DateTime>()),
            Times.Once
        );
        _mockOnPremiseService.Verify(
            s => s.GetCalendarItemsAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()),
            Times.Never
        );
    }

    [Fact]
    public async Task SyncAllMailboxesAsync_ShouldUpdateSyncStatus()
    {
        // Arrange
        var service = new CalendarSyncService(
            _mockLogger.Object,
            _mockOnPremiseService.Object,
            _mockOnlineSourceService.Object,
            _mockOnlineService.Object,
            _syncSettings,
            _statusService,
            _mockStateRepository.Object
        );

        var mappings = new List<MailboxMapping>
        {
            new()
            {
                SourceMailbox = "test@example.com",
                DestinationMailbox = "test@cloud.com",
                SourceType = SourceType.ExchangeOnPremise
            }
        };

        _mockOnPremiseService
            .Setup(s => s.GetCalendarItemsAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarItemSync>());

        // Act
        await service.SyncAllMailboxesAsync(mappings);

        // Assert
        var status = _statusService.GetStatus();
        status.IsRunning.Should().BeFalse(); // Should be false after completion
        status.LastSyncTime.Should().NotBeNull();
    }

    [Fact]
    public async Task SyncAllMailboxesAsync_ShouldPersistState()
    {
        // Arrange
        var service = new CalendarSyncService(
            _mockLogger.Object,
            _mockOnPremiseService.Object,
            _mockOnlineSourceService.Object,
            _mockOnlineService.Object,
            _syncSettings,
            _statusService,
            _mockStateRepository.Object
        );

        var mappings = new List<MailboxMapping>
        {
            new()
            {
                SourceMailbox = "test@example.com",
                DestinationMailbox = "test@cloud.com",
                SourceType = SourceType.ExchangeOnPremise
            }
        };

        _mockOnPremiseService
            .Setup(s => s.GetCalendarItemsAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarItemSync>());

        // Act
        await service.SyncAllMailboxesAsync(mappings);

        // Assert
        _mockStateRepository.Verify(r => r.LoadStateAsync(), Times.Once);
        _mockStateRepository.Verify(r => r.SaveStateAsync(It.IsAny<PersistedSyncState>()), Times.Once);
    }
}
