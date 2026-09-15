using System.Globalization;
using BuildingBlocks.Messaging.CorrelationId;
using BuildingBlocks.Messaging.RequestReply;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rebus.Config;

namespace BuildingBlocks.Messaging;

/// <summary>
/// The shared Rebus/RabbitMQ setup every host calls, so "which transport, which config key,
/// which pipeline steps" is decided once (ARCHITECTURE.md "Messaging") instead of four times.
/// </summary>
public static class RebusConfigurationExtensions
{
    /// <summary>
    /// The configuration key the RabbitMQ connection string is read from. Fixed by the compose
    /// file as the <c>Rebus__ConnectionString</c> environment variable — do not rename.
    /// </summary>
    public const string ConnectionStringConfigKey = "Rebus:ConnectionString";

    /// <summary>
    /// Optional key overriding how long <see cref="IRequestReplyBridge"/> waits for a reply, in
    /// seconds (<c>Rebus__ReplyTimeoutSeconds</c> as an environment variable). Seconds rather
    /// than a <see cref="TimeSpan"/> string on purpose: <c>TimeSpan.Parse("5")</c> is five
    /// <i>days</i>, which is precisely the wrong failure mode for a timeout.
    /// </summary>
    public const string ReplyTimeoutSecondsConfigKey = "Rebus:ReplyTimeoutSeconds";

    /// <summary>The ~5s of ARCHITECTURE.md "Command → event flow", used when the key above is not set.</summary>
    private static readonly TimeSpan DefaultReplyTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Registers Rebus over RabbitMQ, with correlation-ID propagation and the request/reply
    /// bridge enabled, as the default bus. Additional configuration (sagas, timeouts,
    /// subscriptions, ...) can be layered on via <paramref name="configure"/>.
    /// </summary>
    /// <param name="inputQueueName">This service's own queue name (its command inbox).</param>
    /// <param name="configure">
    /// Optional further configuration, applied after the transport and correlation-ID step are
    /// set up. Receives the <see cref="IServiceProvider"/> so it can pull in e.g. an
    /// <see cref="MongoDB.Driver.IMongoDatabase"/> already registered by the host.
    /// </param>
    public static IServiceCollection AddBuildingBlocksRebus(
        this IServiceCollection services,
        IConfiguration configuration,
        string inputQueueName,
        Func<RebusConfigurer, IServiceProvider, RebusConfigurer>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputQueueName);

        var connectionString = configuration[ConnectionStringConfigKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing required configuration value '{ConnectionStringConfigKey}' " +
                "(set via the 'Rebus__ConnectionString' environment variable).");
        }

        services.AddSingleton<CorrelationIdAccessor>();
        services.AddSingleton<ICorrelationIdAccessor>(sp => sp.GetRequiredService<CorrelationIdAccessor>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(new RequestReplyOptions(ReadReplyTimeout(configuration)));
        services.AddSingleton<PendingRequestRegistry>();
        services.AddSingleton<IRequestReplyBridge, RequestReplyBridge>();

        services.AddRebus((rebus, serviceProvider) =>
        {
            var accessor = serviceProvider.GetRequiredService<ICorrelationIdAccessor>();
            var pendingRequests = serviceProvider.GetRequiredService<PendingRequestRegistry>();

            var configurer = rebus
                .Transport(t => t.UseRabbitMq(connectionString, inputQueueName))
                .Options(o =>
                {
                    o.EnableCorrelationIdPropagation(accessor);
                    o.EnableRequestReplyBridge(pendingRequests);
                });

            return configure is null ? configurer : configure(configurer, serviceProvider);
        });

        return services;
    }

    private static TimeSpan ReadReplyTimeout(IConfiguration configuration)
    {
        var configured = configuration[ReplyTimeoutSecondsConfigKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultReplyTimeout;
        }

        // Misconfiguration fails at startup rather than degrading into a bridge that never times
        // out (or times out instantly) once a user is waiting on it.
        if (!double.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            !double.IsFinite(seconds) ||
            seconds <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration value '{ReplyTimeoutSecondsConfigKey}' must be a positive number " +
                $"of seconds, but was '{configured}'.");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
