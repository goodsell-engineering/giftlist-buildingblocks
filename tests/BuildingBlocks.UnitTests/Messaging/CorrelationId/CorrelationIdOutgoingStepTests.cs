using BuildingBlocks.Messaging.CorrelationId;
using BuildingBlocks.UnitTests.TestDoubles;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;

namespace BuildingBlocks.UnitTests.Messaging.CorrelationId;

public sealed class CorrelationIdOutgoingStepTests
{
    [Fact]
    public async Task Process_ShouldStampAmbientCorrelationId_WhenOutgoingMessageHasNone()
    {
        // Arrange
        var accessor = new CorrelationIdAccessor();
        using var scope = accessor.BeginScope("trace-1");
        var step = new CorrelationIdOutgoingStep(accessor);
        var message = new Message(new Dictionary<string, string>(), new object());
        var context = new OutgoingStepContext(
            message,
            new FakeTransactionContext(),
            new DestinationAddresses(["giftlists"]));

        // Act
        await step.Process(context, () => Task.CompletedTask);

        // Assert
        Assert.Equal("trace-1", message.Headers[Headers.CorrelationId]);
    }

    [Fact]
    public async Task Process_ShouldNotOverwriteExistingCorrelationId_WhenOneIsAlreadyFlowing()
    {
        // Arrange
        var accessor = new CorrelationIdAccessor();
        using var scope = accessor.BeginScope("ambient");
        var step = new CorrelationIdOutgoingStep(accessor);
        var headers = new Dictionary<string, string> { [Headers.CorrelationId] = "already-flowing" };
        var message = new Message(headers, new object());
        var context = new OutgoingStepContext(
            message,
            new FakeTransactionContext(),
            new DestinationAddresses(["giftlists"]));

        // Act
        await step.Process(context, () => Task.CompletedTask);

        // Assert
        Assert.Equal("already-flowing", message.Headers[Headers.CorrelationId]);
    }

    [Fact]
    public async Task Process_ShouldLeaveHeadersUnset_WhenNoAmbientCorrelationIdIsActive()
    {
        // Arrange
        var accessor = new CorrelationIdAccessor();
        var step = new CorrelationIdOutgoingStep(accessor);
        var message = new Message(new Dictionary<string, string>(), new object());
        var context = new OutgoingStepContext(
            message,
            new FakeTransactionContext(),
            new DestinationAddresses(["giftlists"]));

        // Act
        await step.Process(context, () => Task.CompletedTask);

        // Assert
        Assert.False(message.Headers.ContainsKey(Headers.CorrelationId));
    }
}
