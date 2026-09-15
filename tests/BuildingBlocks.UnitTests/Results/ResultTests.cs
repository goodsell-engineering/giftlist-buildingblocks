using BuildingBlocks.Results;

namespace BuildingBlocks.UnitTests.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_ShouldProduceSuccessfulResult_WhenCalled()
    {
        // Arrange — none

        // Act
        var result = Result.Success();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Failure_ShouldProduceFailedResult_CarryingTheError()
    {
        // Arrange
        var error = new Error("giftlist.expired", "The list has expired.", ErrorKind.Conflict);

        // Act
        var result = Result.Failure(error);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Error_ShouldThrow_WhenResultIsSuccessful()
    {
        // Arrange
        var result = Result.Success();

        // Act
        var exception = Record.Exception(() => result.Error);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void Error_ShouldThrow_WhenResultIsDefault()
    {
        // Arrange — a default(Result), not one built via Success()/Failure(); this is what a
        // `default` switch arm or an uninitialised field produces.
        var result = default(Result);

        // Act
        var exception = Record.Exception(() => result.Error);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ImplicitConversion_ShouldProduceFailedResult_FromError()
    {
        // Arrange
        var error = new Error("giftlist.expired", "The list has expired.", ErrorKind.Conflict);

        // Act
        Result result = error;

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Match_ShouldInvokeOnSuccess_WhenResultIsSuccessful()
    {
        // Arrange
        var result = Result.Success();

        // Act
        var outcome = result.Match(() => "success", _ => "failure");

        // Assert
        Assert.Equal("success", outcome);
    }

    [Fact]
    public void Match_ShouldInvokeOnFailure_WhenResultIsFailed()
    {
        // Arrange
        var error = new Error("giftlist.expired", "The list has expired.", ErrorKind.Conflict);
        var result = Result.Failure(error);

        // Act
        var outcome = result.Match(() => "success", e => e.Code);

        // Assert
        Assert.Equal("giftlist.expired", outcome);
    }

    [Fact]
    public void Equality_ShouldTreatTwoFailuresWithTheSameError_AsEqual()
    {
        // Arrange
        var error = new Error("giftlist.expired", "The list has expired.", ErrorKind.Conflict);
        var first = Result.Failure(error);
        var second = Result.Failure(error);

        // Act
        var areEqual = first == second;

        // Assert
        Assert.True(areEqual);
    }
}
