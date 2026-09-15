using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BuildingBlocks.HealthChecks;

/// <summary>
/// Confirms the configured Mongo database answers a <c>ping</c>. Library code only — GL-52 wires
/// this into each worker host's actual <c>/healthz</c> HTTP endpoint.
/// </summary>
public sealed class MongoHealthCheck(IMongoDatabase database) : IHealthCheck
{
    private static readonly BsonDocumentCommand<BsonDocument> PingCommand = new(new BsonDocument("ping", 1));

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await database.RunCommandAsync(PingCommand, cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("MongoDB ping failed.", exception);
        }
    }
}
