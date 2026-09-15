using BuildingBlocks.Results;

namespace BuildingBlocks.UnitTests.Results;

public sealed class ResultOfTTests
{
    [Fact]
    public void Success_ShouldExposeValue_WhenResultIsSuccessful()
    {
        // Arrange — none

        // Act
        var result = Result<string>.Success("gift-list-42");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("gift-list-42", result.Value);
    }

    [Fact]
    public void Failure_ShouldExposeError_WhenResultIsFailed()
    {
        // Arrange
        var error = new Error("giftlist.notFound", "No such list.", ErrorKind.NotFound);

        // Act
        var result = Result<string>.Failure(error);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Value_ShouldThrow_WhenResultIsFailed()
    {
        // Arrange
        var error = new Error("giftlist.notFound", "No such list.", ErrorKind.NotFound);
        var result = Result<string>.Failure(error);

        // Act
        var exception = Record.Exception(() => result.Value);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void Error_ShouldThrow_WhenResultIsSuccessful()
    {
        // Arrange
        var result = Result<string>.Success("gift-list-42");

        // Act
        var exception = Record.Exception(() => result.Error);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void Error_ShouldThrow_WhenResultIsDefault()
    {
        // Arrange — a default(Result<T>), not one built via Success()/Failure(); this is what a
        // `default` switch arm or an uninitialised field produces.
        var result = default(Result<string>);

        // Act
        var exception = Record.Exception(() => result.Error);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ImplicitConversion_ShouldProduceSuccessfulResult_FromValue()
    {
        // Arrange — none

        // Act
        Result<string> result = "gift-list-42";

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("gift-list-42", result.Value);
    }

    [Fact]
    public void ImplicitConversion_ShouldProduceFailedResult_FromError()
    {
        // Arrange
        var error = new Error("giftlist.notFound", "No such list.", ErrorKind.NotFound);

        // Act
        Result<string> result = error;

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Match_ShouldInvokeOnSuccess_WithTheValue_WhenResultIsSuccessful()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act
        var outcome = result.Match(value => value * 2, _ => -1);

        // Assert
        Assert.Equal(84, outcome);
    }

    [Fact]
    public void Match_ShouldInvokeOnFailure_WithTheError_WhenResultIsFailed()
    {
        // Arrange
        var error = new Error("giftlist.notFound", "No such list.", ErrorKind.NotFound);
        var result = Result<int>.Failure(error);

        // Act
        var outcome = result.Match(value => value.ToString(), e => e.Code);

        // Assert
        Assert.Equal("giftlist.notFound", outcome);
    }

    [Fact]
    public void Equality_ShouldTreatTwoSuccessesWithTheSameValue_AsEqual()
    {
        // Arrange
        var first = Result<string>.Success("gift-list-42");
        var second = Result<string>.Success("gift-list-42");

        // Act
        var areEqual = first == second;

        // Assert
        Assert.True(areEqual);
    }

    [Fact]
    public void Equality_ShouldTreatSuccessAndFailure_AsNotEqual()
    {
        // Arrange
        var error = new Error("giftlist.notFound", "No such list.", ErrorKind.NotFound);
        var success = Result<string>.Success("gift-list-42");
        var failure = Result<string>.Failure(error);

        // Act
        var areEqual = success == failure;

        // Assert
        Assert.False(areEqual);
    }
}
