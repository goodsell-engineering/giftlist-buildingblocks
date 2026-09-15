using BuildingBlocks.Messaging.CorrelationId;

namespace BuildingBlocks.UnitTests.Messaging.CorrelationId;

public sealed class CorrelationIdAccessorTests
{
    [Fact]
    public void CorrelationId_ShouldBeNull_WhenNoScopeIsActive()
    {
        // Arrange
        var accessor = new CorrelationIdAccessor();

        // Act
        var correlationId = accessor.CorrelationId;

        // Assert
        Assert.Null(correlationId);
    }

    [Fact]
    public void BeginScope_ShouldExposeValue_WhileScopeIsActive()
    {
        // Arrange
        var accessor = new CorrelationIdAccessor();

        // Act
        using var scope = accessor.BeginScope("trace-1");

        // Assert
        Assert.Equal("trace-1", accessor.CorrelationId);
    }

    [Fact]
    public void Dispose_ShouldRestorePreviousValue_WhenScopeIsNested()
    {
        // Arrange
        var accessor = new CorrelationIdAccessor();
        using var outer = accessor.BeginScope("outer");

        // Act
        using (accessor.BeginScope("inner"))
        {
            // scope active for this block only
        }

        // Assert
        Assert.Equal("outer", accessor.CorrelationId);
    }

    [Fact]
    public void Dispose_ShouldClearValue_WhenOuterScopeHadNoPreviousValue()
    {
        // Arrange
        var accessor = new CorrelationIdAccessor();
        var scope = accessor.BeginScope("trace-1");

        // Act
        scope.Dispose();

        // Assert
        Assert.Null(accessor.CorrelationId);
    }
}
