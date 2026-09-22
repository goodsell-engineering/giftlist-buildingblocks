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

    [Theory]
    [InlineData("identity", "identity.error")]
    [InlineData("giftlist", "giftlist.error")]
    [InlineData("reservation", "reservation.error")]
    [InlineData("gateway", "gateway.error")]
    public void ErrorQueueName_ShouldAppendAnErrorSuffix_ToTheInputQueueName(string inputQueueName, string expected)
    {
        // Arrange — GL-43: pins the per-service naming (CONVENTIONS.md "Persistence" fixes these
        // four as the services' own singular queue names) against the exact literal an operator
        // would go looking for in the RabbitMQ management UI.

        // Act
        var errorQueueName = RebusConfigurationExtensions.ErrorQueueName(inputQueueName);

        // Assert
        Assert.Equal(expected, errorQueueName);
    }

    [Fact]
    public void MaxDeliveryAttempts_ShouldBeFive()
    {
        // Arrange — GL-43: pins the chosen attempt count itself, not just that some value exists,
        // so a change to it is a deliberate edit of this test rather than a silent drift.

        // Act
        var maxDeliveryAttempts = RebusConfigurationExtensions.MaxDeliveryAttempts;

        // Assert
        Assert.Equal(5, maxDeliveryAttempts);
    }
}
