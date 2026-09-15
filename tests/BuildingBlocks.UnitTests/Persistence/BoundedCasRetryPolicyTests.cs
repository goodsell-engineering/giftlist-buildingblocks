using BuildingBlocks.Persistence;

namespace BuildingBlocks.UnitTests.Persistence;

/// <summary>
/// GL-76: pins <see cref="BoundedCasRetryPolicy"/>'s own arithmetic directly — the cap, the
/// exponential doubling, the ceiling, the jitter factor — as an ordinary, mock-free unit test.
/// <see cref="BoundedCasRetryTests"/> covers the loop that consults it, also mock-free.
///
/// Moved here from <c>Gateway.UnitTests</c> by GL-79 alongside the production type — see
/// <see cref="BoundedCasRetryPolicy"/>'s own remarks for the MaxAttempts derivation (Gateway's
/// own, not a value to inherit unreflectively) and for the internal-to-public visibility change.
/// Renamed from <c>ProjectionCasRetryPolicyTests</c> alongside the production type (GL-79 review,
/// S3). <c>MaxAttempts</c>/<c>CanRetry</c> are per-instance now, not static (GL-79 review, S1), so
/// every test below constructs its own policy rather than calling through the type.
/// </summary>
public sealed class BoundedCasRetryPolicyTests
{
    /// <summary>
    /// GL-79 review (S2): the default's own value is load-bearing prose in this type's remarks —
    /// three paragraphs of arithmetic that only justifies 8. Nothing else in the repository pins
    /// the literal (every other reference is symbolic, via <c>MaxAttempts</c>/<c>DefaultMaxAttempts</c>
    /// itself), so changing the default silently would leave every other test green. This is the
    /// one place that changes stop being silent.
    /// </summary>
    [Fact]
    public void DefaultMaxAttempts_ShouldBe8_MatchingThisTypesOwnDerivation()
    {
        // Arrange — none

        // Act
        var fromTheConstant = BoundedCasRetryPolicy.DefaultMaxAttempts;
        var fromAnUnconfiguredInstance = new BoundedCasRetryPolicy().MaxAttempts;

        // Assert
        Assert.Equal(8, fromTheConstant);
        Assert.Equal(8, fromAnUnconfiguredInstance);
    }

    [Fact]
    public void Constructor_ShouldUseTheSuppliedMaxAttempts_NotTheDefault()
    {
        // Arrange — none

        // Act
        var policy = new BoundedCasRetryPolicy(maxAttempts: 3);

        // Assert
        Assert.Equal(3, policy.MaxAttempts);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenMaxAttemptsIsLessThanOne()
    {
        // Arrange — none

        // Act
        var exception = Record.Exception(() => new BoundedCasRetryPolicy(maxAttempts: 0));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CanRetry_ShouldReturnTrue_ForEveryAttemptBelowMaxAttempts()
    {
        // Arrange — a small, explicit cap rather than the default: at MaxAttempts == 1,
        // Enumerable.Range(1, MaxAttempts - 1) is empty and Assert.All would pass on nothing, so
        // this pins a cap with at least one attempt actually below it (GL-79 review, S2).
        var policy = new BoundedCasRetryPolicy(maxAttempts: 4);
        var belowMax = Enumerable.Range(1, policy.MaxAttempts - 1).ToList();

        // Act
        var results = belowMax.Select(policy.CanRetry).ToList();

        // Assert
        Assert.NotEmpty(belowMax);
        Assert.All(results, Assert.True);
    }

    [Fact]
    public void CanRetry_ShouldReturnFalse_OnceMaxAttemptsIsReached()
    {
        // Arrange
        var policy = new BoundedCasRetryPolicy(maxAttempts: 4);

        // Act
        var atMax = policy.CanRetry(policy.MaxAttempts);
        var pastMax = policy.CanRetry(policy.MaxAttempts + 1);

        // Assert
        Assert.False(atMax);
        Assert.False(pastMax);
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 40)]
    [InlineData(3, 80)]
    [InlineData(4, 160)]
    [InlineData(5, 320)] // 20ms * 2^4 = 320ms — exactly at the ceiling, not yet past it
    [InlineData(6, 320)] // 20ms * 2^5 = 640ms would exceed the ceiling — capped
    [InlineData(7, 320)]
    public void BaseDelayFor_ShouldDoubleThenHoldAtTheCeiling(int attempt, int expectedMilliseconds)
    {
        // Arrange — none; BaseDelayFor is static, the doubling/ceiling shape having nothing to do
        // with any one instance's MaxAttempts or jitter source.

        // Act
        var delay = BoundedCasRetryPolicy.BaseDelayFor(attempt);

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), delay);
    }

    [Fact]
    public void DelayFor_ShouldApplyTheMinimumJitterFactor_WhenTheJitterSourceReturnsZero()
    {
        // Arrange — a fixed jitter source, not a seeded Random, so the EXACT resulting delay can
        // be pinned rather than only its range.
        var policy = new BoundedCasRetryPolicy(jitterSource: () => 0.0);

        // Act
        var delay = policy.DelayFor(1);

        // Assert — factor 0.5 + 0.0 = 0.5, against BaseDelayFor(1)'s 20ms.
        Assert.Equal(TimeSpan.FromMilliseconds(10), delay);
    }

    [Fact]
    public void DelayFor_ShouldApplyTheMaximumJitterFactor_WhenTheJitterSourceReturnsItsUpperBound()
    {
        // Arrange — jitterSource's own contract is [0, 1), so 1.0 is one past its documented
        // upper bound; used here only to pin the arithmetic at the edge, not to claim the real
        // source (Random.Shared.NextDouble) can ever produce it.
        var policy = new BoundedCasRetryPolicy(jitterSource: () => 1.0);

        // Act
        var delay = policy.DelayFor(1);

        // Assert — factor 0.5 + 1.0 = 1.5, against BaseDelayFor(1)'s 20ms.
        Assert.Equal(TimeSpan.FromMilliseconds(30), delay);
    }
}
