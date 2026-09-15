using BuildingBlocks.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.UnitTests.Messaging;

public sealed class RebusConfigurationExtensionsTests
{
    [Fact]
    public void AddBuildingBlocksRebus_ShouldThrow_WhenConnectionStringIsMissing()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var exception = Record.Exception(
            () => services.AddBuildingBlocksRebus(configuration, "giftlists"));

        // Assert
        var invalidOperationException = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains(RebusConfigurationExtensions.ConnectionStringConfigKey, invalidOperationException.Message);
    }

    [Fact]
    public void AddBuildingBlocksRebus_ShouldThrow_WhenConnectionStringIsBlank()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RebusConfigurationExtensions.ConnectionStringConfigKey] = "   ",
            })
            .Build();

        // Act
        var exception = Record.Exception(
            () => services.AddBuildingBlocksRebus(configuration, "giftlists"));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void AddBuildingBlocksRebus_ShouldThrow_WhenInputQueueNameIsMissing()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RebusConfigurationExtensions.ConnectionStringConfigKey] = "amqp://guest:guest@localhost:5672",
            })
            .Build();

        // Act
        var exception = Record.Exception(
            () => services.AddBuildingBlocksRebus(configuration, string.Empty));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void AddBuildingBlocksRebus_ShouldNotThrow_WhenConfigurationIsValid()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RebusConfigurationExtensions.ConnectionStringConfigKey] = "amqp://guest:guest@localhost:5672",
            })
            .Build();

        // Act
        var exception = Record.Exception(
            () => services.AddBuildingBlocksRebus(configuration, "giftlists"));

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-5")]
    public void AddBuildingBlocksRebus_ShouldThrow_WhenReplyTimeoutSecondsIsInvalid(string replyTimeoutSeconds)
    {
        // Arrange — GL-57: ReadReplyTimeout's fail-fast guard had zero coverage. It runs
        // synchronously inside AddBuildingBlocksRebus, before Rebus ever touches a connection,
        // so this is provable without a broker.
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RebusConfigurationExtensions.ConnectionStringConfigKey] = "amqp://guest:guest@localhost:5672",
                [RebusConfigurationExtensions.ReplyTimeoutSecondsConfigKey] = replyTimeoutSeconds,
            })
            .Build();

        // Act
        var exception = Record.Exception(
            () => services.AddBuildingBlocksRebus(configuration, "giftlists"));

        // Assert
        var invalidOperationException = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains(RebusConfigurationExtensions.ReplyTimeoutSecondsConfigKey, invalidOperationException.Message);
    }

    [Fact]
    public void AddBuildingBlocksRebus_ShouldNotThrow_WhenReplyTimeoutSecondsIsAPositiveNumber()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RebusConfigurationExtensions.ConnectionStringConfigKey] = "amqp://guest:guest@localhost:5672",
                [RebusConfigurationExtensions.ReplyTimeoutSecondsConfigKey] = "2.5",
            })
            .Build();

        // Act
        var exception = Record.Exception(
            () => services.AddBuildingBlocksRebus(configuration, "giftlists"));

        // Assert
        Assert.Null(exception);
    }
}
