using BuildingBlocks.Messaging.CorrelationId;
using BuildingBlocks.UnitTests.TestDoubles;
using Rebus.Messages;
using Rebus.Pipeline;

namespace BuildingBlocks.UnitTests.Messaging.CorrelationId;

public sealed class CorrelationIdIncomingStepTests
{
    [Fact]
    public async Task Process_ShouldExposeHeaderValue_WhileNextIsRunning()
    {
        // Arrange
        var accessor = new CorrelationIdAccessor();
        var step = new CorrelationIdIncomingStep(accessor);
        var headers = new Dictionary<string, string> { [Headers.CorrelationId] = "trace-1" };
        var context = new IncomingStepContext(
            new TransportMessage(headers, []),
            new FakeTransactionContext());
        string? observedDuringHandling = null;

        // Act
        await step.Process(context, () =>
        {
            observedDuringHandling = accessor.CorrelationId;
            return Task.CompletedTask;
        });

        // Assert
        Assert.Equal("trace-1", observedDuringHandling);
    }

    [Fact]
    public async Task Process_ShouldRestoreAmbientValue_AfterNextCompletes()
    {
        // Arrange
        var accessor = new CorrelationIdAccessor();
        var step = new CorrelationIdIncomingStep(accessor);
        var headers = new Dictionary<string, string> { [Headers.CorrelationId] = "trace-1" };
        var context = new IncomingStepContext(
            new TransportMessage(headers, []),
            new FakeTransactionContext());

        // Act
        await step.Process(context, () => Task.CompletedTask);

        // Assert
        Assert.Null(accessor.CorrelationId);
    }

    [Fact]
    public async Task Process_ShouldExposeNull_WhenTransportMessageHasNoCorrelationIdHeader()
    {
        // Arrange
        var accessor = new CorrelationIdAccessor();
        var step = new CorrelationIdIncomingStep(accessor);
        var context = new IncomingStepContext(
            new TransportMessage([], []),
            new FakeTransactionContext());
        var observedDuringHandling = "not-yet-observed";

        // Act
        await step.Process(context, () =>
        {
            observedDuringHandling = accessor.CorrelationId;
            return Task.CompletedTask;
        });

        // Assert
        Assert.Null(observedDuringHandling);
    }
}
