using BuildingBlocks.Messaging.RequestReply;
using BuildingBlocks.Results;

namespace BuildingBlocks.UnitTests.Messaging.RequestReply;

public sealed class ReplyFaultTests
{
    [Fact]
    public void From_ShouldCarryTheErrorsCodeMessageAndKindName()
    {
        // Arrange
        var error = new Error("identity.email_already_registered", "A user with this email is already registered.", ErrorKind.Conflict);

        // Act
        var fault = ReplyFault.From(error);

        // Assert
        Assert.Equal("identity.email_already_registered", fault.Code);
        Assert.Equal("A user with this email is already registered.", fault.Message);
        Assert.Equal(nameof(ErrorKind.Conflict), fault.Kind);
    }

    [Fact]
    public void ToError_ShouldReconstructTheOriginalError_Roundtrip()
    {
        // Arrange
        var original = new Error("identity.invalid_credentials", "Email or password is incorrect.", ErrorKind.Unauthenticated);
        var fault = ReplyFault.From(original);

        // Act
        var reconstructed = fault.ToError();

        // Assert
        Assert.Equal(original, reconstructed);
    }

    [Fact]
    public void ToError_ShouldMapToUnavailable_WhenKindIsNotRecognised()
    {
        // Arrange — a member name from a newer service this process's BuildingBlocks doesn't
        // know about yet; must degrade to a failure, never throw.
        var fault = new ReplyFault("some.code", "some message", "SomeFutureKind");

        // Act
        var error = fault.ToError();

        // Assert
        Assert.Equal(ErrorKind.Unavailable, error.Kind);
        Assert.Equal("some.code", error.Code);
        Assert.Equal("some message", error.Message);
    }

    [Fact]
    public void ToError_ShouldMapToUnavailable_WhenKindIsTheUnderlyingIntegerOfAnUndefinedMember()
    {
        // Arrange — Enum.TryParse alone accepts the string form of the underlying integer even
        // when no member has that value; untrusted wire data reaching a value like this must
        // still degrade to Unavailable rather than reaching ErrorKindTransportMapping's throwing
        // default (GL-53 review finding).
        var fault = new ReplyFault("some.code", "some message", "42");

        // Act
        var error = fault.ToError();

        // Assert
        Assert.Equal(ErrorKind.Unavailable, error.Kind);
        Assert.Equal("some.code", error.Code);
        Assert.Equal("some message", error.Message);
    }

    [Fact]
    public void From_ShouldThrowArgumentNullException_WhenErrorIsNull()
    {
        // Arrange — none

        // Act
        var exception = Record.Exception(() => ReplyFault.From(null!));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }
}
