using BuildingBlocks.IntegrationTests.Fixtures;
using BuildingBlocks.Testing;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace BuildingBlocks.IntegrationTests.Messaging;

/// <summary>
/// GL-80: pins <see cref="QueueCleanup.DeleteAsync"/> itself, not just its call sites. GL-75
/// consolidated four independently-written copies of this ~15-line snippet into one shared
/// helper that every service's IntegrationTests now depends on (GiftLists, Identity, Gateway,
/// and this project's own <see cref="Support.TestRebusHost"/>) — but none of those ~80 tests
/// observes queue cleanup at all, so all of them keep passing even if
/// <see cref="QueueCleanup.DeleteAsync"/> is turned into a no-op. This test talks to the broker
/// directly, bypassing every one of those call sites, so it is the only thing in the repository
/// that would notice.
/// </summary>
[Collection(RabbitMqCollection.Name)]
public sealed class QueueCleanupTests(RabbitMqFixture rabbitMq)
{
    [Fact]
    public async Task DeleteAsync_ShouldRemoveTheQueueFromTheBroker_WhenItWasDeclared()
    {
        // Arrange — declare a queue directly against the broker (never through QueueCleanup),
        // and capture whether it is actually there before DeleteAsync ever runs. Without this
        // capture, a passing "queue is gone afterwards" assertion would be satisfied just as
        // well by a queue that was never created in the first place.
        var queueName = $"gl80.{Guid.NewGuid():N}";
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using (var declareChannel = await connection.CreateChannelAsync())
        {
            await declareChannel.QueueDeclareAsync(queueName, durable: false, exclusive: false, autoDelete: false);
        }
        var existedBeforeDelete = await QueueExistsAsync(connection, queueName);

        // Act
        await QueueCleanup.DeleteAsync(rabbitMq.ConnectionString, queueName);

        // Assert
        Assert.True(existedBeforeDelete);
        var existsAfterDelete = await QueueExistsAsync(connection, queueName);
        Assert.False(existsAfterDelete);
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnNormally_WhenTheQueueWasNeverDeclared()
    {
        // Arrange — a queue name guaranteed never to have been declared by anything. Pins the
        // broker's own idempotency: QueueDeleteAsync(ifUnused: false, ifEmpty: false) against a
        // queue that was never declared returns normally rather than raising a 404 (verified by
        // mutation — removing DeleteAsync's try/catch entirely leaves this test green, so the
        // guarantee lives in the broker call, not in a catch clause). Every call site relies on
        // this: TestRebusHost's teardown, for one, calls DeleteAsync whether or not the host it
        // is disposing ever got as far as declaring its queue.
        var queueName = $"gl80.never-declared.{Guid.NewGuid():N}";

        // Act
        var exception = await Record.ExceptionAsync(
            () => QueueCleanup.DeleteAsync(rabbitMq.ConnectionString, queueName));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// A passive declare succeeds without side effects if the queue exists, and throws
    /// (closing the channel it ran on) if it does not — used here purely as an existence probe
    /// for this test's own assertions, independent of <see cref="QueueCleanup.DeleteAsync"/>,
    /// which no longer has a catch of its own to relate this to (GL-80: proven unreachable for
    /// its stated purpose, then removed). Always opens a fresh channel, since a failed passive
    /// declare leaves the one it ran on unusable.
    /// </summary>
    private static async Task<bool> QueueExistsAsync(IConnection connection, string queueName)
    {
        await using var channel = await connection.CreateChannelAsync();
        try
        {
            await channel.QueueDeclarePassiveAsync(queueName);
            return true;
        }
        catch (OperationInterruptedException)
        {
            return false;
        }
    }
}
