using BuildingBlocks.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.UnitTests.HealthChecks;

public sealed class RabbitMqHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ShouldReturnUnhealthy_WhenBrokerIsUnreachable()
    {
        // Arrange — port 1 is a reserved, never-listening TCP port, so the connection fails
        // fast without needing a real broker or a timeout.
        var healthCheck = new RabbitMqHealthCheck("amqp://guest:guest@127.0.0.1:1");
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }
}
