using ExchangeCalendarSync.Models;
using FluentAssertions;
using Xunit;

namespace ExchangeCalendarSync.Tests.Models;

public class CalendarItemSyncTests
{
    [Fact]
    public void CalendarItemSync_DefaultValues_ShouldBeInitialized()
    {
        // Act
        var item = new CalendarItemSync();

        // Assert
        item.Id.Should().BeEmpty();
        item.Subject.Should().BeEmpty();
        item.Location.Should().BeEmpty();
        item.SourceMailbox.Should().BeEmpty();
        item.DestinationMailbox.Should().BeEmpty();
        item.RequiredAttendees.Should().NotBeNull().And.BeEmpty();
        item.OptionalAttendees.Should().NotBeNull().And.BeEmpty();
        item.IsAllDay.Should().BeFalse();
        item.IsRecurring.Should().BeFalse();
    }

    [Fact]
    public void CalendarItemSync_CanSetAllProperties()
    {
        // Arrange & Act
        var item = new CalendarItemSync
        {
            Id = "test-id",
            Subject = "Test Meeting",
            Body = "Test Description",
            Start = new DateTime(2024, 1, 1, 10, 0, 0),
            End = new DateTime(2024, 1, 1, 11, 0, 0),
            Location = "Conference Room",
            IsAllDay = false,
            RequiredAttendees = new List<string> { "user1@example.com" },
            OptionalAttendees = new List<string> { "user2@example.com" },
            Organizer = "organizer@example.com",
            Categories = "Business",
            IsRecurring = true,
            RecurrencePattern = "Daily",
            LastModified = new DateTime(2024, 1, 1),
            SourceMailbox = "source@example.com",
            DestinationMailbox = "dest@cloud.com"
        };

        // Assert
        item.Id.Should().Be("test-id");
        item.Subject.Should().Be("Test Meeting");
        item.Body.Should().Be("Test Description");
        item.Start.Should().Be(new DateTime(2024, 1, 1, 10, 0, 0));
        item.End.Should().Be(new DateTime(2024, 1, 1, 11, 0, 0));
        item.Location.Should().Be("Conference Room");
        item.IsAllDay.Should().BeFalse();
        item.RequiredAttendees.Should().ContainSingle().Which.Should().Be("user1@example.com");
        item.OptionalAttendees.Should().ContainSingle().Which.Should().Be("user2@example.com");
        item.Organizer.Should().Be("organizer@example.com");
        item.Categories.Should().Be("Business");
        item.IsRecurring.Should().BeTrue();
        item.RecurrencePattern.Should().Be("Daily");
        item.LastModified.Should().Be(new DateTime(2024, 1, 1));
        item.SourceMailbox.Should().Be("source@example.com");
        item.DestinationMailbox.Should().Be("dest@cloud.com");
    }
}
