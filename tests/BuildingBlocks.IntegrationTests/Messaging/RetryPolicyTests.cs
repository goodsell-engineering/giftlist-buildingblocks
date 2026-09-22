using System.Text;
using BuildingBlocks.IntegrationTests.Fixtures;
using BuildingBlocks.IntegrationTests.Support;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Routing.TypeBased;

namespace BuildingBlocks.IntegrationTests.Messaging;

/// <summary>
/// GL-43: the retry/error-queue policy <see cref="RebusConfigurationExtensions"/> now chooses
/// explicitly, proven against a real broker — CONVENTIONS.md "Testing" bans an in-memory
/// transport here precisely because retry, redelivery and the error queue "only exist on a real
/// transport" (ARCHITECTURE.md "IntegrationTests: all infrastructure real via Testcontainers").
/// </summary>
[Collection(RabbitMqCollection.Name)]
public sealed class RetryPolicyTests(RabbitMqFixture rabbitMq)
{
    [Fact]
    public async Task Handle_ShouldSucceed_WhenATransientFailureRecoversWithinTheRetryBudget()
    {
        // Arrange — fails on every attempt but the last one the policy allows, so a retry
        // ceiling one attempt too low would turn this into a message that never succeeds.
        var attempts = new AttemptTracker();
        await using var responder = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            services => services
                .AddSingleton(attempts)
                .AddRebusHandler<TransientlyFailingHandler>());
        await using var requester = await StartRequesterRoutedTo(responder.InputQueueName);

        // Act
        await requester.Bus.Send(new ProbeRequest(ProbeRequestHandler.Success));
        var succeeded = await attempts.Succeeded.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // Assert
        Assert.True(succeeded);
        Assert.Equal(RebusConfigurationExtensions.MaxDeliveryAttempts, attempts.Count);
    }

    [Fact]
    public async Task Handle_ShouldLandInTheServiceErrorQueue_WithDeliveryCountAtTheCeiling_WhenTheHandlerNeverSucceeds()
    {
        // Arrange
        var attempts = new AttemptTracker();
        await using var responder = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            services => services
                .AddSingleton(attempts)
                .AddRebusHandler<AlwaysFailingHandler>());
        await using var requester = await StartRequesterRoutedTo(responder.InputQueueName);
        var errorQueueName = RebusConfigurationExtensions.ErrorQueueName(responder.InputQueueName);

        // Act
        await requester.Bus.Send(new ProbeRequest(ProbeRequestHandler.Success));
        var deadLettered = await PollForMessageAsync(errorQueueName, TimeSpan.FromSeconds(20));

        // Assert — the ceiling was actually spent, not skipped past (which is what the fail-fast
        // path below is for), and the message still names where it came from.
        Assert.NotNull(deadLettered);
        Assert.Equal(RebusConfigurationExtensions.MaxDeliveryAttempts, attempts.Count);
        Assert.Equal(
            RebusConfigurationExtensions.MaxDeliveryAttempts.ToString(),
            HeaderValue(deadLettered, "rbs2-delivery-count"));
        Assert.Equal(responder.InputQueueName, HeaderValue(deadLettered, "rbs2-source-queue"));
        Assert.Contains("deliberately never succeeds", HeaderValue(deadLettered, "rbs2-error-details"));
    }

    [Fact]
    public async Task Handle_ShouldFailFastToTheServiceErrorQueue_WithoutExhaustingRetries_WhenTheMessageBodyCannotBeDeserialized()
    {
        // Arrange — no handler is registered at all: FailFastOn<JsonException> means this never
        // gets far enough to need one. The body is published directly against the broker,
        // bypassing Rebus's own serializer entirely, which is the only way to get genuinely
        // malformed bytes onto the wire (Rebus's own Send would just fail to serialize a valid
        // .NET object in the first place).
        await using var responder = await TestRebusHost.StartAsync(rabbitMq.ConnectionString);
        var errorQueueName = RebusConfigurationExtensions.ErrorQueueName(responder.InputQueueName);
        var messageTypeHeader = $"{typeof(ProbeRequest).FullName}, {typeof(ProbeRequest).Assembly.GetName().Name}";

        // Act
        await PublishMalformedMessageAsync(responder.InputQueueName, messageTypeHeader);
        var deadLettered = await PollForMessageAsync(errorQueueName, TimeSpan.FromSeconds(20));

        // Assert — delivery count of 1 is the proof this skipped the retry ceiling rather than
        // merely reaching the same place the slow way; the test above already covers that path.
        Assert.NotNull(deadLettered);
        Assert.Equal("1", HeaderValue(deadLettered, "rbs2-delivery-count"));
    }

    /// <summary>
    /// Mirrors <see cref="Messaging.RequestReply.RequestReplyBridgeTests"/>'s own helper: a real
    /// service routes on its Contracts assembly, these tests share <see cref="ProbeRequest"/>
    /// across many responders, so each requester maps it explicitly to the one it targets.
    /// </summary>
    private Task<TestRebusHost> StartRequesterRoutedTo(string responderQueueName) =>
        TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            configureRebus: configurer => configurer.Routing(r => r.TypeBased().Map<ProbeRequest>(responderQueueName)));

    private async Task PublishMalformedMessageAsync(string queueName, string messageTypeHeader)
    {
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?>
            {
                ["rbs2-msg-id"] = Guid.NewGuid().ToString(),
                ["rbs2-msg-type"] = messageTypeHeader,
                ["rbs2-content-type"] = "application/json;charset=utf-8",
            },
        };
        var body = Encoding.UTF8.GetBytes("{ this is not valid json");

        // The default AMQP exchange routes to a queue of the same name via an implicit binding
        // every declared queue gets, independent of whatever exchange Rebus itself binds the
        // queue to — so this reaches the host's own input queue without needing to know Rebus's
        // internal exchange topology.
        await channel.BasicPublishAsync(exchange: string.Empty, routingKey: queueName, body: body, mandatory: false, basicProperties: properties);
    }

    /// <summary>
    /// Polls the named queue with a fresh channel each attempt — the queue may not exist yet the
    /// first time this runs (Rebus's error handler declares it lazily, on the first message that
    /// actually needs it), and a channel RabbitMQ has closed under it (a 404 against a queue that
    /// is not there yet) cannot be reused for the next attempt.
    /// </summary>
    private async Task<BasicGetResult?> PollForMessageAsync(string queueName, TimeSpan timeout)
    {
        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var channel = await connection.CreateChannelAsync();
                var result = await channel.BasicGetAsync(queueName, autoAck: true);
                if (result is not null)
                {
                    return result;
                }
            }
            catch (OperationInterruptedException)
            {
                // The queue does not exist yet — try again on the next poll.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return null;
    }

    private static string HeaderValue(BasicGetResult message, string headerKey)
    {
        var value = message.BasicProperties.Headers?[headerKey];
        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            null => string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>Counts attempts and signals the one that is allowed to succeed.</summary>
    private sealed class AttemptTracker
    {
        private int _count;

        public int Count => _count;

        public TaskCompletionSource<bool> Succeeded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Increment() => Interlocked.Increment(ref _count);
    }

    /// <summary>
    /// Fails on every attempt but the last the policy allows, then succeeds — the transient
    /// case: a message that recovers inside the retry budget.
    /// </summary>
    private sealed class TransientlyFailingHandler(AttemptTracker attempts) : IHandleMessages<ProbeRequest>
    {
        public Task Handle(ProbeRequest message)
        {
            var attempt = attempts.Increment();
            if (attempt < RebusConfigurationExtensions.MaxDeliveryAttempts)
            {
                throw new InvalidOperationException($"Deliberately transient failure on attempt {attempt}.");
            }

            attempts.Succeeded.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    /// <summary>Fails on every attempt — the case that genuinely exhausts the retry ceiling.</summary>
    private sealed class AlwaysFailingHandler(AttemptTracker attempts) : IHandleMessages<ProbeRequest>
    {
        public Task Handle(ProbeRequest message)
        {
            attempts.Increment();
            throw new InvalidOperationException("This handler deliberately never succeeds.");
        }
    }
}
