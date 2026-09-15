using BuildingBlocks.Messaging.RequestReply;

namespace BuildingBlocks.UnitTests.Messaging.RequestReply;

/// <summary>
/// Pure in-memory logic, no Rebus pipeline or broker involved — the registry's own contract, as
/// distinct from the wire behaviour covered end-to-end in BuildingBlocks.IntegrationTests.
/// </summary>
public sealed class PendingRequestRegistryTests
{
    [Fact]
    public void Register_ShouldThrowInvalidOperationException_WhenRequestIdAlreadyPending()
    {
        // Arrange
        var registry = new PendingRequestRegistry();
        const string requestId = "duplicate-id";
        using var first = registry.Register(requestId);

        // Act
        var exception = Record.Exception(() => registry.Register(requestId));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void TryComplete_ShouldReturnFalse_WhenNobodyIsWaiting()
    {
        // Arrange
        var registry = new PendingRequestRegistry();

        // Act
        var completed = registry.TryComplete("nobody-waiting", new object());

        // Assert
        Assert.False(completed);
    }

    [Fact]
    public async Task TryComplete_ShouldReturnTrueAndReleaseTheWaiter_WhenRegistered()
    {
        // Arrange
        var registry = new PendingRequestRegistry();
        const string requestId = "req-1";
        using var pending = registry.Register(requestId);
        var reply = new object();

        // Act
        var completed = registry.TryComplete(requestId, reply);

        // Assert
        Assert.True(completed);
        Assert.Same(reply, await pending.Reply);
    }

    [Fact]
    public void Dispose_ShouldUnregisterTheRequest_SoTheSameIdCanBeReusedAfterward()
    {
        // Arrange — the leak PendingRequest's own doc comment warns about: Dispose is the only
        // removal path, so a request id that was never freed would make a second Register under
        // the same id throw forever, not just once.
        var registry = new PendingRequestRegistry();
        const string requestId = "reused-id";
        var pending = registry.Register(requestId);

        // Act
        pending.Dispose();
        var exception = Record.Exception(() => registry.Register(requestId).Dispose());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Registry_ShouldDrainToZero_AfterSuccessTimeoutAndCancellationStylePaths()
    {
        // Arrange — three requests standing in for the bridge's three exit paths: a completed
        // reply, a request nobody ever completes (the timeout path), and one abandoned outright
        // (the cancellation path). All three leave the same way: Dispose.
        var registry = new PendingRequestRegistry();
        var completed = registry.Register("completed");
        var timedOut = registry.Register("timed-out");
        var cancelled = registry.Register("cancelled");
        registry.TryComplete("completed", new object());

        // Act
        completed.Dispose();
        timedOut.Dispose();
        cancelled.Dispose();

        // Assert
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void TryComplete_ShouldThrowArgumentNullException_WhenReplyIsNull()
    {
        // Arrange
        var registry = new PendingRequestRegistry();
        using var pending = registry.Register("req-null-reply");

        // Act
        var exception = Record.Exception(() => registry.TryComplete("req-null-reply", null!));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }
}
