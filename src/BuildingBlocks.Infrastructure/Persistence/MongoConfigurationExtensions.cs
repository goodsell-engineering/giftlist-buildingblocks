using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace BuildingBlocks.Persistence;

/// <summary>
/// The shared Mongo client/database setup every host calls, so "which config key names the
/// connection string" is decided once instead of guessed four times. Mirrors
/// <see cref="Messaging.RebusConfigurationExtensions"/>.
/// </summary>
public static class MongoConfigurationExtensions
{
    /// <summary>
    /// The configuration key the Mongo connection string is read from. Fixed by the compose
    /// file as the <c>ConnectionStrings__Mongo</c> environment variable — do not rename.
    /// </summary>
    public const string ConnectionStringConfigKey = "ConnectionStrings:Mongo";

    /// <summary>
    /// Registers <see cref="IMongoClient"/> and <see cref="IMongoDatabase"/> (both singletons —
    /// the driver's client already pools connections internally) and applies the shared BSON
    /// conventions, so no host can forget to call <see cref="MongoConventions.Register"/>.
    /// </summary>
    /// <param name="databaseName">This service's own database (ARCHITECTURE.md "Tech stack" — database-per-service).</param>
    public static IServiceCollection AddBuildingBlocksMongo(
        this IServiceCollection services,
        IConfiguration configuration,
        string databaseName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var connectionString = configuration[ConnectionStringConfigKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing required configuration value '{ConnectionStringConfigKey}' " +
                "(set via the 'ConnectionStrings__Mongo' environment variable).");
        }

        MongoConventions.Register();

        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

        return services;
    }
}
