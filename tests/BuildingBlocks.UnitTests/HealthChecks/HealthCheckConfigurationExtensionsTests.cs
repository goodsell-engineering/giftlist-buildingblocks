using BuildingBlocks.HealthChecks;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.UnitTests.HealthChecks;

public sealed class HealthCheckConfigurationExtensionsTests
{
    [Fact]
    public void AddBuildingBlocksHealthChecks_ShouldThrow_WhenConnectionStringIsMissing()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var exception = Record.Exception(
            () => services.AddBuildingBlocksHealthChecks(configuration));

        // Assert
        var invalidOperationException = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains(RebusConfigurationExtensions.ConnectionStringConfigKey, invalidOperationException.Message);
    }

    [Fact]
    public void AddBuildingBlocksHealthChecks_ShouldRegisterBothChecks_WhenConfigurationIsValid()
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
        services.AddBuildingBlocksHealthChecks(configuration);
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        // Assert
        Assert.Contains(registrations, r => r.Name == HealthCheckConfigurationExtensions.MongoCheckName);
        Assert.Contains(registrations, r => r.Name == HealthCheckConfigurationExtensions.RabbitMqCheckName);
    }
}
