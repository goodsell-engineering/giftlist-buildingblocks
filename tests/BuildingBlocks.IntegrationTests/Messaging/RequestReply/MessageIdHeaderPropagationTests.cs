using BuildingBlocks.IntegrationTests.Fixtures;
using BuildingBlocks.IntegrationTests.Support;
using BuildingBlocks.Messaging.RequestReply;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Config;
using Rebus.Messages;
using Rebus.Routing.TypeBased;

namespace BuildingBlocks.IntegrationTests.Messaging.RequestReply;

/// <summary>
/// The highest-value test in GL-57: the one assumption the entire bridge design rests on
/// (<see cref="RequestReplyBridge"/>'s own remarks — "an explicitly set id survives to the wire"
/// — and <see cref="PendingRequestRegistry"/>'s remarks on keying by that same id) is otherwise
/// asserted only in a code comment. It is provable only against a real broker: an in-memory
/// transport hands the <see cref="Message"/> straight to the receiving side and never actually
/// serializes it onto RabbitMQ headers, so it could never catch this regressing.
/// </summary>
[Collection(RabbitMqCollection.Name)]
public sealed class MessageIdHeaderPropagationTests(RabbitMqFixture rabbitMq)
{
    [Fact]
    public async Task ExplicitlySetMessageId_ShouldSurviveTheWire_AndComeBackAsInReplyTo()
    {
        // Arrange — two real processes, real queues, nothing routed through the
        // IRequestReplyBridge itself: this bypasses it deliberately, sending with a
        // caller-chosen rbs2-message-id via the raw IBus.Send the bridge itself uses
        // internally, and registering a waiter under that exact id via the same
        // PendingRequestRegistry PendingReplyIncomingStep matches against. If Rebus's own
        // AssignDefaultHeadersStep ever started overwriting an already-set message id, or the
        // RabbitMQ transport ever stopped round-tripping rbs2-in-reply-to, this is what would
        // catch it — nothing about IRequestReplyBridge's own code path is exercised here.
        await using var responder = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            services => services.AddRebusHandler<ProbeRequestHandler>());
        await using var requester = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            configureRebus: configurer =>
                configurer.Routing(r => r.TypeBased().Map<ProbeRequest>(responder.InputQueueName)));

        var explicitMessageId = $"explicit-{Guid.NewGuid():N}";
        using var pending = requester.PendingRequests.Register(explicitMessageId);

        // Act
        await requester.Bus.Send(
            new ProbeRequest(ProbeRequestHandler.Success),
            new Dictionary<string, string> { [Headers.MessageId] = explicitMessageId });
        var reply = await pending.Reply.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert — the requester's own PendingReplyIncomingStep only ever completes a
        // PendingRequest by matching rbs2-in-reply-to against the id it was Register()ed under,
        // so reaching this line at all already proves the header round-tripped; the reply's
        // shape confirms it was genuinely this exchange, not some other stray message.
        var probeReply = Assert.IsType<ProbeReply>(reply);
        Assert.Equal("ok", probeReply.Value);
    }

    [Fact]
    public async Task ExplicitlySetMessageId_ShouldArriveUnchanged_OnTheOutgoingLegAcrossTheBroker()
    {
        // Arrange — the complementary half of the test above: that one proves the *incoming*
        // leg (a reply's rbs2-in-reply-to matches what was Register()ed), this one inspects the
        // header a real responder actually received, so a future AssignDefaultHeadersStep
        // change that overwrote an already-set id would be caught here even if some other
        // coincidence made the round trip above still happen to match.
        var capture = new HeaderCapture();
        await using var responder = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            services =>
            {
                services.AddSingleton(capture);
                services.AddRebusHandler<HeaderCapturingHandler>();
            });
        await using var requester = await TestRebusHost.StartAsync(
            rabbitMq.ConnectionString,
            configureRebus: configurer =>
                configurer.Routing(r => r.TypeBased().Map<ProbeRequest>(responder.InputQueueName)));

        var explicitMessageId = $"explicit-{Guid.NewGuid():N}";

        // Act
        await requester.Bus.Send(
            new ProbeRequest(ProbeRequestHandler.Success),
            new Dictionary<string, string> { [Headers.MessageId] = explicitMessageId });
        var receivedMessageId = await capture.Completion.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Equal(explicitMessageId, receivedMessageId);
    }
}
