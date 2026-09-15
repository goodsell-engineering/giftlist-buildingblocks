using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.RequestReply;
using BuildingBlocks.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Config;

namespace BuildingBlocks.IntegrationTests.Support;

/// <summary>
/// One process's worth of real Rebus/RabbitMQ wiring, built through the exact same
/// <see cref="RebusConfigurationExtensions.AddBuildingBlocksRebus"/> every service host calls —
/// this is what makes these tests exercise the bridge's real composition, not a re-implementation
/// of it. Each instance gets its own randomly-named input queue so tests never need to purge a
/// shared one; the queue is deleted from the broker on <see cref="DisposeAsync"/> so a long-lived,
/// <c>.WithReuse(true)</c> local container doesn't accumulate empty queues across runs.
/// </summary>
internal sealed class TestRebusHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly string _connectionString;

    public string InputQueueName { get; }

    public IBus Bus => _host.Services.GetRequiredService<IBus>();

    public IRequestReplyBridge RequestReplyBridge => _host.Services.GetRequiredService<IRequestReplyBridge>();

    /// <summary>
    /// Internal (GL-57): the registry the requester and responder halves of the bridge meet at —
    /// see BuildingBlocks.Infrastructure's InternalsVisibleTo for why this project can see it.
    /// </summary>
    public PendingRequestRegistry PendingRequests => _host.Services.GetRequiredService<PendingRequestRegistry>();

    private TestRebusHost(IHost host, string connectionString, string inputQueueName)
    {
        _host = host;
        _connectionString = connectionString;
        InputQueueName = inputQueueName;
    }

    public static async Task<TestRebusHost> StartAsync(
        string rabbitConnectionString,
        Action<IServiceCollection>? configureHandlers = null,
        TimeSpan? replyTimeout = null,
        Func<RebusConfigurer, RebusConfigurer>? configureRebus = null)
    {
        var inputQueueName = $"test.{Guid.NewGuid():N}";

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        var configValues = new Dictionary<string, string?>
        {
            [RebusConfigurationExtensions.ConnectionStringConfigKey] = rabbitConnectionString,
        };
        if (replyTimeout is { } timeout)
        {
            configValues[RebusConfigurationExtensions.ReplyTimeoutSecondsConfigKey] =
                timeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        builder.Configuration.AddInMemoryCollection(configValues);
        builder.Services.AddBuildingBlocksRebus(
            builder.Configuration,
            inputQueueName,
            configure: configureRebus is null ? null : (configurer, _) => configureRebus(configurer));
        configureHandlers?.Invoke(builder.Services);

        var host = builder.Build();
        await host.StartAsync();

        return new TestRebusHost(host, rabbitConnectionString, inputQueueName);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        // Purges this host's own queue from the broker rather than leaving it behind — the
        // per-test isolation mechanism CONVENTIONS.md "Testing" asks for, adapted for RabbitMQ: unique
        // queue names make cross-test interference impossible, and deleting them keeps a reused
        // local container from accumulating one abandoned queue per test run. Shared with every
        // other service's equivalent cleanup (GL-75) rather than reimplemented here.
        await QueueCleanup.DeleteAsync(_connectionString, InputQueueName);
    }
}
