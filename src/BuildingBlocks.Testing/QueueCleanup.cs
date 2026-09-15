using RabbitMQ.Client;

namespace BuildingBlocks.Testing;

/// <summary>
/// Deletes a RabbitMQ queue directly against the broker, bypassing Rebus entirely.
///
/// GL-75: this is the fifth writing of a ~15-line snippet that had independently accreted in
/// four places (GiftLists.IntegrationTests, BuildingBlocks.IntegrationTests' TestRebusHost,
/// Identity.IntegrationTests' UserEventPublishingTests, and Gateway.IntegrationTests per GL-70)
/// — extracted here, with all four call sites deleted, rather than adding a fifth. On a
/// <c>.WithReuse(true)</c> local container, a per-test/per-run queue (and, for a subscriber, its
/// topic binding) left behind here would accumulate across every local run and keep routing
/// future integration events into a queue nobody drains. Restarting the container between runs
/// would also fix that, but CONVENTIONS.md "Testing" asks for isolation by dropping state, not by
/// restarting containers.
///
/// GL-80: this used to swallow <see cref="RabbitMQ.Client.Exceptions.OperationInterruptedException"/>
/// on the theory that deleting a queue that was never declared (e.g. a host/subscriber that
/// failed to start before it got that far) would raise a 404 the caller shouldn't care about.
/// Proven false by test: <c>QueueDeleteAsync(ifUnused: false, ifEmpty: false)</c> against a real
/// broker is idempotent — deleting a queue that never existed returns normally, no exception
/// raised, nothing to catch. What that catch actually swallowed instead was every other
/// channel-level AMQP failure — ACCESS_REFUSED, RESOURCE_LOCKED, NOT_ALLOWED, a broker going
/// away mid-call — each a genuine cleanup failure, and CONVENTIONS.md "Messaging" is explicit that this
/// codebase never swallows an exception just to keep going. Removed; a real cleanup failure now
/// surfaces instead of leaving an orphaned queue behind silently, which is the exact leak GL-75
/// built this helper to stop.
/// </summary>
public static class QueueCleanup
{
    public static async Task DeleteAsync(string rabbitMqConnectionString, string queueName)
    {
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeleteAsync(queueName, ifUnused: false, ifEmpty: false);
    }
}
