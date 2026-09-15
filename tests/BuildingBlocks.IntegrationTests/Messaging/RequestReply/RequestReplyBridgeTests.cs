using System.Diagnostics;
using BuildingBlocks.IntegrationTests.Fixtures;
using BuildingBlocks.IntegrationTests.Support;
using BuildingBlocks.Results;
using Rebus.Config;
using Rebus.Routing.TypeBased;

namespace BuildingBlocks.IntegrationTests.Messaging.RequestReply;

/// <summary>
/// The requester half of ARCHITECTURE.md "Command → event flow", end to end against a real broker: two real
/// processes (their own Rebus bus, their own queue), talking only through RabbitMQ — never an
/// in-memory transport (CONVENTIONS.md "Testing").
/// </summary>
[Collection(RabbitMqCollection.Name)]
public sealed class RequestReplyBridgeTests(RabbitMqFixture rabbitMq)
{
    [Fact]
    public async Task SendAndAwaitReply_ShouldReturnTheTypedReply_WhenTheHandlerReplies()
    {
        // Arrange
        await using var responder = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            services => services.AddRebusHandler<ProbeRequestHandler>());
        await using var requester = await StartRequesterRoutedTo(responder.InputQueueName);

        // Act
        var result = await requester.RequestReplyBridge.SendAndAwaitReply<ProbeReply>(
            new ProbeRequest(ProbeRequestHandler.Success));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value.Value);
    }

    [Fact]
    public async Task SendAndAwaitReply_ShouldReturnAFailureResult_WhenTheHandlerRepliesWithAFault()
    {
        // Arrange
        await using var responder = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            services => services.AddRebusHandler<ProbeRequestHandler>());
        await using var requester = await StartRequesterRoutedTo(responder.InputQueueName);

        // Act
        var result = await requester.RequestReplyBridge.SendAndAwaitReply<ProbeReply>(
            new ProbeRequest(ProbeRequestHandler.Fault));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ProbeRequestHandler.FaultError.Code, result.Error.Code);
        Assert.Equal(ProbeRequestHandler.FaultError.Message, result.Error.Message);
        Assert.Equal(ProbeRequestHandler.FaultError.Kind, result.Error.Kind);
    }

    [Fact]
    public async Task SendAndAwaitReply_ShouldTimeOutAfterWaitingRoughlyTheConfiguredBudget_WhenNoReplyArrivesInTime()
    {
        // Arrange — an explicit timeout, so a mis-parsed or zeroed budget that returned
        // instantly with the same code/kind would still be caught by the elapsed-time assertion
        // below rather than reading as a pass.
        await using var responder = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            services => services.AddRebusHandler<ProbeRequestHandler>());
        await using var requester = await StartRequesterRoutedTo(responder.InputQueueName);
        var budget = TimeSpan.FromSeconds(2);
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await requester.RequestReplyBridge.SendAndAwaitReply<ProbeReply>(
            new ProbeRequest(ProbeRequestHandler.NeverReplies),
            budget);
        stopwatch.Stop();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("messaging.reply_timeout", result.Error.Code);
        Assert.Equal(ErrorKind.Unavailable, result.Error.Kind);
        Assert.True(
            stopwatch.Elapsed >= budget - TimeSpan.FromMilliseconds(300),
            $"Expected to wait close to the {budget} budget before giving up; only waited {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task SendAndAwaitReply_ShouldUseTheConfiguredDefaultTimeout_WhenNoExplicitTimeoutIsGiven()
    {
        // Arrange — GL-57 review: Rebus:ReplyTimeoutSeconds/ReadReplyTimeout's configured-default
        // path had zero coverage; every other test here passes an explicit override.
        var configuredTimeout = TimeSpan.FromSeconds(2);
        await using var responder = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            services => services.AddRebusHandler<ProbeRequestHandler>());
        await using var requester = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            replyTimeout: configuredTimeout,
            configureRebus: configurer =>
                configurer.Routing(r => r.TypeBased().Map<ProbeRequest>(responder.InputQueueName)));
        var stopwatch = Stopwatch.StartNew();

        // Act — no timeout argument: this must fall back to the RequestReplyOptions built from
        // Rebus:ReplyTimeoutSeconds, not BuildingBlocks' own ~5s default.
        var result = await requester.RequestReplyBridge.SendAndAwaitReply<ProbeReply>(
            new ProbeRequest(ProbeRequestHandler.NeverReplies));
        stopwatch.Stop();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("messaging.reply_timeout", result.Error.Code);
        Assert.True(
            stopwatch.Elapsed >= configuredTimeout - TimeSpan.FromMilliseconds(300) &&
            stopwatch.Elapsed < TimeSpan.FromSeconds(4),
            $"Expected to wait close to the configured {configuredTimeout} default, not " +
            $"BuildingBlocks' own ~5s fallback; waited {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task SendAndAwaitReply_ShouldThrowOperationCanceledException_WhenTheCallerCancels()
    {
        // Arrange
        await using var responder = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            services => services.AddRebusHandler<ProbeRequestHandler>());
        await using var requester = await StartRequesterRoutedTo(responder.InputQueueName);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        var exception = await Record.ExceptionAsync(() => requester.RequestReplyBridge.SendAndAwaitReply<ProbeReply>(
            new ProbeRequest(ProbeRequestHandler.NeverReplies),
            TimeSpan.FromSeconds(30),
            cts.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task PendingRequests_ShouldContainTheRequestWhileWaiting_AndDrainToZeroAfterTheTimeout()
    {
        // Arrange — two-sided on purpose (GL-57 review): asserting only the post-completion
        // Count == 0 would also pass if the bridge had silently stopped registering waiters at
        // all, which is exactly the regression this test exists to catch. Asserting Count == 1
        // while the request is still in flight rules that out.
        await using var responder = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            services => services.AddRebusHandler<ProbeRequestHandler>());
        await using var requester = await StartRequesterRoutedTo(responder.InputQueueName);

        // Act
        var pendingReply = requester.RequestReplyBridge.SendAndAwaitReply<ProbeReply>(
            new ProbeRequest(ProbeRequestHandler.NeverReplies), TimeSpan.FromSeconds(3));
        var countWhileInFlight = requester.PendingRequests.Count;
        var result = await pendingReply;
        var countAfterCompletion = requester.PendingRequests.Count;

        // Assert
        Assert.Equal(1, countWhileInFlight);
        Assert.True(result.IsFailure);
        Assert.Equal(0, countAfterCompletion);
    }

    /// <summary>
    /// A real service routes on its Contracts assembly's namespace; these tests share one
    /// message type (<see cref="ProbeRequest"/>) across many differently-queued responders per
    /// test, so each requester maps it explicitly to the one responder it should talk to —
    /// ordinary Rebus type-based routing, set up the same way a service's own
    /// <c>configure</c> callback to <c>AddBuildingBlocksRebus</c> would.
    /// </summary>
    private Task<TestRebusHost> StartRequesterRoutedTo(string responderQueueName) =>
        TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            configureRebus: configurer => configurer.Routing(r => r.TypeBased().Map<ProbeRequest>(responderQueueName)));
}
