using BuildingBlocks.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;

namespace BuildingBlocks.HealthChecks;

/// <summary>
/// Registers the shared readiness checks (Mongo, RabbitMQ) so GL-52 can wire one implementation
/// across all three worker hosts instead of three. This only registers the
/// <see cref="IHealthCheck"/>s with the DI container — mapping them to the actual
/// <c>/healthz</c> HTTP endpoint is GL-52's job, out of scope here.
/// </summary>
public static class HealthCheckConfigurationExtensions
{
    public const string MongoCheckName = "mongodb";
    public const string RabbitMqCheckName = "rabbitmq";

    /// <summary>
    /// Registers the Mongo and RabbitMQ readiness checks, both tagged <c>"ready"</c>.
    /// Requires an <see cref="IMongoDatabase"/> to already be registered by the host.
    /// </summary>
    public static IHealthChecksBuilder AddBuildingBlocksHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration[RebusConfigurationExtensions.ConnectionStringConfigKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing required configuration value '{RebusConfigurationExtensions.ConnectionStringConfigKey}' " +
                "(set via the 'Rebus__ConnectionString' environment variable).");
        }

        return services
            .AddHealthChecks()
            .Add(new HealthCheckRegistration(
                MongoCheckName,
                sp => new MongoHealthCheck(sp.GetRequiredService<IMongoDatabase>()),
                failureStatus: null,
                tags: ["ready"]))
            .Add(new HealthCheckRegistration(
                RabbitMqCheckName,
                _ => new RabbitMqHealthCheck(connectionString),
                failureStatus: null,
                tags: ["ready"]));
    }
}
