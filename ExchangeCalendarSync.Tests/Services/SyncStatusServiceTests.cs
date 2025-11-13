using ExchangeCalendarSync.Services;
using FluentAssertions;
using Xunit;

namespace ExchangeCalendarSync.Tests.Services;

public class SyncStatusServiceTests
{
    [Fact]
    public void GetStatus_InitialState_ShouldReturnDefaultValues()
    {
        // Arrange
        var service = new SyncStatusService();

        // Act
        var status = service.GetStatus();

        // Assert
        status.Should().NotBeNull();
        status.IsRunning.Should().BeFalse();
        status.LastSyncTime.Should().BeNull();
        status.NextScheduledSync.Should().BeNull();
        status.TotalItemsSynced.Should().Be(0);
        status.TotalErrors.Should().Be(0);
        status.MailboxStatuses.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void StartSync_ShouldSetIsRunningToTrue()
    {
        // Arrange
        var service = new SyncStatusService();

        // Act
        service.StartSync();
        var status = service.GetStatus();

        // Assert
        status.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void EndSync_ShouldSetIsRunningToFalseAndUpdateLastSyncTime()
    {
        // Arrange
        var service = new SyncStatusService();
        var beforeEnd = DateTime.UtcNow;
        service.StartSync();

        // Act
        service.EndSync();
        var afterEnd = DateTime.UtcNow;
        var status = service.GetStatus();

        // Assert
        status.IsRunning.Should().BeFalse();
        status.LastSyncTime.Should().NotBeNull();
        status.LastSyncTime!.Value.Should().BeOnOrAfter(beforeEnd).And.BeOnOrBefore(afterEnd);
    }

    [Fact]
    public void SetNextScheduledSync_ShouldUpdateNextScheduledSync()
    {
        // Arrange
        var service = new SyncStatusService();
        var nextSync = DateTime.UtcNow.AddMinutes(5);

        // Act
        service.SetNextScheduledSync(nextSync);
        var status = service.GetStatus();

        // Assert
        status.NextScheduledSync.Should().Be(nextSync);
    }

    [Fact]
    public void UpdateMailboxStatus_NewMailbox_ShouldCreateNewStatus()
    {
        // Arrange
        var service = new SyncStatusService();

        // Act
        service.UpdateMailboxStatus("test@example.com", 10, 2, "Completed");
        var status = service.GetStatus();

        // Assert
        status.MailboxStatuses.Should().ContainKey("test@example.com");
        var mailboxStatus = status.MailboxStatuses["test@example.com"];
        mailboxStatus.MailboxEmail.Should().Be("test@example.com");
        mailboxStatus.ItemsSynced.Should().Be(10);
        mailboxStatus.Errors.Should().Be(2);
        mailboxStatus.Status.Should().Be("Completed");
        mailboxStatus.LastSyncTime.Should().NotBeNull();
        status.TotalItemsSynced.Should().Be(10);
        status.TotalErrors.Should().Be(2);
    }

    [Fact]
    public void UpdateMailboxStatus_ExistingMailbox_ShouldAccumulateStats()
    {
        // Arrange
        var service = new SyncStatusService();
        service.UpdateMailboxStatus("test@example.com", 10, 2, "Completed");

        // Act
        service.UpdateMailboxStatus("test@example.com", 5, 1, "Completed");
        var status = service.GetStatus();

        // Assert
        var mailboxStatus = status.MailboxStatuses["test@example.com"];
        mailboxStatus.ItemsSynced.Should().Be(15); // 10 + 5
        mailboxStatus.Errors.Should().Be(3); // 2 + 1
        status.TotalItemsSynced.Should().Be(15);
        status.TotalErrors.Should().Be(3);
    }

    [Fact]
    public void IsRunning_WhenNotRunning_ShouldReturnFalse()
    {
        // Arrange
        var service = new SyncStatusService();

        // Act & Assert
        service.IsRunning().Should().BeFalse();
    }

    [Fact]
    public void IsRunning_WhenRunning_ShouldReturnTrue()
    {
        // Arrange
        var service = new SyncStatusService();
        service.StartSync();

        // Act & Assert
        service.IsRunning().Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireSyncLock_WhenNotLocked_ShouldReturnTrue()
    {
        // Arrange
        var service = new SyncStatusService();

        // Act
        var acquired = await service.TryAcquireSyncLock();

        // Assert
        acquired.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireSyncLock_WhenAlreadyLocked_ShouldReturnFalse()
    {
        // Arrange
        var service = new SyncStatusService();
        await service.TryAcquireSyncLock();

        // Act
        var acquired = await service.TryAcquireSyncLock();

        // Assert
        acquired.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseSyncLock_ShouldAllowReacquisition()
    {
        // Arrange
        var service = new SyncStatusService();
        await service.TryAcquireSyncLock();
        service.ReleaseSyncLock();

        // Act
        var acquired = await service.TryAcquireSyncLock();

        // Assert
        acquired.Should().BeTrue();
    }
}
