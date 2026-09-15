using BuildingBlocks.Messaging.RequestReply;
using BuildingBlocks.Results;

namespace BuildingBlocks.UnitTests.Messaging.RequestReply;

public sealed class RequestReplyErrorsTests
{
    private sealed record ProbeRequest;

    [Fact]
    public void ReplyTimedOut_ShouldUseTheStableTimeoutCode()
    {
        // Arrange — none

        // Act
        var error = RequestReplyErrors.ReplyTimedOut(typeof(ProbeRequest), TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(RequestReplyErrors.ReplyTimeoutCode, error.Code);
        Assert.Equal("messaging.reply_timeout", error.Code);
    }

    [Fact]
    public void ReplyTimedOut_ShouldBeUnavailable_SoItMapsToTheStillWorkingOnItFallback()
    {
        // Arrange — none

        // Act
        var error = RequestReplyErrors.ReplyTimedOut(typeof(ProbeRequest), TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(ErrorKind.Unavailable, error.Kind);
    }

    [Fact]
    public void ReplyTimedOut_ShouldNameTheRequestTypeAndTimeout_InItsMessage()
    {
        // Arrange — none

        // Act
        var error = RequestReplyErrors.ReplyTimedOut(typeof(ProbeRequest), TimeSpan.FromSeconds(5));

        // Assert
        Assert.Contains(nameof(ProbeRequest), error.Message, StringComparison.Ordinal);
        Assert.Contains("5", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplyTimedOut_ShouldThrowArgumentNullException_WhenRequestTypeIsNull()
    {
        // Arrange — none

        // Act
        var exception = Record.Exception(() => RequestReplyErrors.ReplyTimedOut(null!, TimeSpan.FromSeconds(5)));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }
}
