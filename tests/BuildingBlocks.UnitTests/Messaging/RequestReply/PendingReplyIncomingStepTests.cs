using BuildingBlocks.Messaging.RequestReply;
using BuildingBlocks.UnitTests.TestDoubles;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Pipeline;

namespace BuildingBlocks.UnitTests.Messaging.RequestReply;

/// <summary>
/// Exercises <see cref="PendingReplyIncomingStep.Process"/> directly against a hand-built
/// pipeline context, the same pattern as <c>CorrelationIdIncomingStepTests</c> — no Rebus
/// pipeline or transport needed for the step's own branching logic. Whether the header this step
/// reads actually survives a real broker round trip is a different question, answered only in
/// BuildingBlocks.IntegrationTests (a real broker is the only thing that can answer it).
/// </summary>
public sealed class PendingReplyIncomingStepTests
{
    private static readonly ILog Log = new NullLoggerFactory().GetLogger<PendingReplyIncomingStep>();

    [Fact]
    public async Task Process_ShouldCompleteTheWaiterAndNotCallNext_WhenInReplyToMatchesAPendingRequest()
    {
        // Arrange
        var registry = new PendingRequestRegistry();
        using var pending = registry.Register("request-1");
        var step = new PendingReplyIncomingStep(registry, Log);
        var body = new object();
        var context = BuildContext(new Dictionary<string, string> { [Headers.InReplyTo] = "request-1" }, body);
        var nextCalled = false;

        // Act
        await step.Process(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Assert
        Assert.False(nextCalled);
        Assert.Same(body, await pending.Reply);
    }

    [Fact]
    public async Task Process_ShouldCallNext_WhenMessageHasNoInReplyToHeader()
    {
        // Arrange — an ordinary command/event, not a reply; must pass straight through.
        var registry = new PendingRequestRegistry();
        var step = new PendingReplyIncomingStep(registry, Log);
        var context = BuildContext(new Dictionary<string, string>(), new object());
        var nextCalled = false;

        // Act
        await step.Process(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Process_ShouldNotCallNext_WhenInReplyToMatchesNoPendingRequest()
    {
        // Arrange — the caller already timed out, or this is a redelivery; discarded, not
        // handed to a handler (there is no handler for a reply).
        var registry = new PendingRequestRegistry();
        var step = new PendingReplyIncomingStep(registry, Log);
        var context = BuildContext(new Dictionary<string, string> { [Headers.InReplyTo] = "nobody-waiting" }, new object());
        var nextCalled = false;

        // Act
        await step.Process(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Assert
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Process_ShouldCallNext_WhenInReplyToHeaderIsWhitespace()
    {
        // Arrange
        var registry = new PendingRequestRegistry();
        var step = new PendingReplyIncomingStep(registry, Log);
        var context = BuildContext(new Dictionary<string, string> { [Headers.InReplyTo] = "   " }, new object());
        var nextCalled = false;

        // Act
        await step.Process(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Assert
        Assert.True(nextCalled);
    }

    private static IncomingStepContext BuildContext(Dictionary<string, string> headers, object body)
    {
        var context = new IncomingStepContext(new TransportMessage(headers, []), new FakeTransactionContext());
        context.Save(new Message(headers, body));
        return context;
    }
}
