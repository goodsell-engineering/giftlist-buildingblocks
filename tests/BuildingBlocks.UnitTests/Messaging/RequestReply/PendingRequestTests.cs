using BuildingBlocks.Messaging.RequestReply;

namespace BuildingBlocks.UnitTests.Messaging.RequestReply;

public sealed class PendingRequestTests
{
    [Fact]
    public void TryComplete_ShouldReturnFalse_WhenAlreadyCompleted()
    {
        // Arrange — Rebus's at-least-once delivery means a reply can be redelivered; the second
        // arrival must not be treated as an error.
        var registry = new PendingRequestRegistry();
        using var pending = registry.Register("redelivered");
        pending.TryComplete(new object());

        // Act
        var secondCompletion = pending.TryComplete(new object());

        // Assert
        Assert.False(secondCompletion);
    }

    [Fact]
    public void RequestId_ShouldBeTheIdItWasRegisteredUnder()
    {
        // Arrange
        var registry = new PendingRequestRegistry();

        // Act
        using var pending = registry.Register("my-request-id");

        // Assert
        Assert.Equal("my-request-id", pending.RequestId);
    }

    [Fact]
    public void Dispose_ShouldBeSafeToCallTwice()
    {
        // Arrange
        var registry = new PendingRequestRegistry();
        var pending = registry.Register("dispose-twice");

        // Act
        pending.Dispose();
        var exception = Record.Exception(pending.Dispose);

        // Assert
        Assert.Null(exception);
    }
}
