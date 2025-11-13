using ExchangeCalendarSync.Models;
using FluentAssertions;
using Xunit;

namespace ExchangeCalendarSync.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void MailboxMapping_DefaultSourceType_ShouldBeExchangeOnPremise()
    {
        // Arrange & Act
        var mapping = new MailboxMapping
        {
            SourceMailbox = "test@example.com",
            DestinationMailbox = "test@cloud.com"
        };

        // Assert
        mapping.SourceType.Should().Be(SourceType.ExchangeOnPremise);
    }

    [Fact]
    public void GetMailboxMappings_WithMailboxesToMonitor_ShouldReturnMappings()
    {
        // Arrange
        var settings = new ExchangeOnPremiseSettings
        {
            MailboxesToMonitor = new List<string>
            {
                "user1@example.com",
                "user2@example.com"
            }
        };

        // Act
        var mappings = settings.GetMailboxMappings();

        // Assert
        mappings.Should().HaveCount(2);
        mappings[0].SourceMailbox.Should().Be("user1@example.com");
        mappings[0].DestinationMailbox.Should().Be("user1@example.com");
        mappings[0].SourceType.Should().Be(SourceType.ExchangeOnPremise);
        mappings[1].SourceMailbox.Should().Be("user2@example.com");
        mappings[1].DestinationMailbox.Should().Be("user2@example.com");
    }

    [Fact]
    public void GetMailboxMappings_WithExplicitMappings_ShouldReturnMappings()
    {
        // Arrange
        var settings = new ExchangeOnPremiseSettings
        {
            MailboxMappings = new List<MailboxMapping>
            {
                new()
                {
                    SourceMailbox = "source@example.com",
                    DestinationMailbox = "dest@cloud.com",
                    SourceType = SourceType.ExchangeOnline
                }
            }
        };

        // Act
        var mappings = settings.GetMailboxMappings();

        // Assert
        mappings.Should().HaveCount(1);
        mappings[0].SourceMailbox.Should().Be("source@example.com");
        mappings[0].DestinationMailbox.Should().Be("dest@cloud.com");
        mappings[0].SourceType.Should().Be(SourceType.ExchangeOnline);
    }

    [Fact]
    public void GetMailboxMappings_WithBothFormats_ShouldCombineWithoutDuplicates()
    {
        // Arrange
        var settings = new ExchangeOnPremiseSettings
        {
            MailboxesToMonitor = new List<string>
            {
                "user1@example.com",
                "user2@example.com"
            },
            MailboxMappings = new List<MailboxMapping>
            {
                new()
                {
                    SourceMailbox = "user1@example.com", // Duplicate
                    DestinationMailbox = "user1@cloud.com",
                    SourceType = SourceType.ExchangeOnPremise
                },
                new()
                {
                    SourceMailbox = "user3@example.com", // New
                    DestinationMailbox = "user3@cloud.com",
                    SourceType = SourceType.ExchangeOnline
                }
            }
        };

        // Act
        var mappings = settings.GetMailboxMappings();

        // Assert
        mappings.Should().HaveCount(3); // user1 from explicit (not duplicated), user2 from monitor, user3 from explicit
        mappings.Should().Contain(m => m.SourceMailbox == "user1@example.com" && m.DestinationMailbox == "user1@cloud.com");
        mappings.Should().Contain(m => m.SourceMailbox == "user2@example.com" && m.DestinationMailbox == "user2@example.com");
        mappings.Should().Contain(m => m.SourceMailbox == "user3@example.com" && m.DestinationMailbox == "user3@cloud.com");
    }

    [Fact]
    public void GetMailboxMappings_EmptyConfiguration_ShouldReturnEmptyList()
    {
        // Arrange
        var settings = new ExchangeOnPremiseSettings();

        // Act
        var mappings = settings.GetMailboxMappings();

        // Assert
        mappings.Should().BeEmpty();
    }

    [Fact]
    public void SourceType_Enum_ShouldHaveExpectedValues()
    {
        // Assert
        Enum.IsDefined(typeof(SourceType), SourceType.ExchangeOnPremise).Should().BeTrue();
        Enum.IsDefined(typeof(SourceType), SourceType.ExchangeOnline).Should().BeTrue();
        ((int)SourceType.ExchangeOnPremise).Should().Be(0);
        ((int)SourceType.ExchangeOnline).Should().Be(1);
    }
}
