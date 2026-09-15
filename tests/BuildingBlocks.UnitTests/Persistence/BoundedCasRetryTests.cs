using BuildingBlocks.Persistence;

namespace BuildingBlocks.UnitTests.Persistence;

/// <summary>
/// GL-76: pins <see cref="BoundedCasRetry"/>'s own attempt-count/backoff/exhaustion SHAPE with a
/// trivial delegate standing in for a real compare-and-set attempt. No Mongo, no mock: this type
/// never touches <c>IMongoCollection</c> at all, so nothing here needs to.
/// <see cref="BoundedCasRetryPolicyTests"/> covers the other half, the arithmetic this type
/// consults.
///
/// Moved here from <c>Gateway.UnitTests</c> by GL-79, alongside the production type itself — see
/// <see cref="BoundedCasRetry"/>'s own doc comment for why, and for the internal-to-public
/// visibility change that means reaching it from here needs no <c>InternalsVisibleTo</c> grant at
/// all, unlike the grant this suite's Gateway equivalent used to need.
/// <c>Gateway.UnitTests/GiftLists/GiftListProjectionRepositoryRetryWiringTests</c> keeps the one
/// test that pins Gateway's own production wiring (<c>GiftListProjectionApplyExhaustedException</c>)
/// rather than this type's generic shape — that one belongs at the consumer, not the library.
/// </summary>
public sealed class BoundedCasRetryTests
{
    /// <summary>Zero jitter, applied here purely to keep this test's real (not simulated) backoff delays small and exact rather than to test the policy itself — <see cref="BoundedCasRetryPolicyTests"/> owns that.</summary>
    private static BoundedCasRetryPolicy FastPolicy() => new(jitterSource: () => 0.0);

    [Fact]
    public async Task RunAsync_ShouldReturn_AfterTheFirstSuccessfulAttempt()
    {
        // Arrange
        var attemptCalls = 0;
        var exhaustedFactoryCalls = 0;

        // Act
        await BoundedCasRetry.RunAsync(
            FastPolicy(),
            attempt =>
            {
                attemptCalls++;
                return Task.FromResult(true);
            },
            exhaustedException: () =>
            {
                exhaustedFactoryCalls++;
                return new InvalidOperationException("should not exhaust");
            },
            CancellationToken.None);

        // Assert — one attempt, no backoff wait, exhaustedException never even called.
        Assert.Equal(1, attemptCalls);
        Assert.Equal(0, exhaustedFactoryCalls);
    }

    [Fact]
    public async Task RunAsync_ShouldStopAfterTheAttemptThatSucceeds_EvenIfEarlierOnesFailed()
    {
        // Arrange — fails twice, then succeeds; a redelivered event landing on the third try.
        var attemptNumbers = new List<int>();

        // Act
        await BoundedCasRetry.RunAsync(
            FastPolicy(),
            attempt =>
            {
                attemptNumbers.Add(attempt);
                return Task.FromResult(attempt == 3);
            },
            exhaustedException: () => new InvalidOperationException("should not exhaust"),
            CancellationToken.None);

        // Assert
        Assert.Equal([1, 2, 3], attemptNumbers);
    }

    [Fact]
    public async Task RunAsync_ShouldThrowFromExhaustedException_WhenEveryAttemptReportsFailure()
    {
        // Arrange — never succeeds, so every one of the policy's own MaxAttempts is exercised;
        // exhaustedException is what a real caller wires to its own exhaustion exception, stood
        // in here by a distinct marker exception so this test proves ONLY that RunAsync's own
        // shape defers to it, not what any particular caller's factory happens to produce.
        var policy = FastPolicy();
        var attemptCalls = 0;

        // Act
        var exception = await Record.ExceptionAsync(() => BoundedCasRetry.RunAsync(
            policy,
            attempt =>
            {
                attemptCalls++;
                return Task.FromResult(false);
            },
            exhaustedException: () => new MarkerException(),
            CancellationToken.None));

        // Assert
        Assert.IsType<MarkerException>(exception);
        Assert.Equal(policy.MaxAttempts, attemptCalls);
    }

    /// <summary>
    /// GL-79 review (S1): the loop is bounded by whatever <c>maxAttempts</c> the CALLER's policy
    /// instance carries, not a value shared across every caller — pinned here with a cap that is
    /// deliberately not the production default (8), so this test would fail if <c>RunAsync</c>
    /// ever regressed to reading a static/shared cap instead of <paramref name="policy"/>'s own.
    /// </summary>
    [Fact]
    public async Task RunAsync_ShouldExhaustAtThePolicysOwnMaxAttempts_NotAnySharedDefault()
    {
        // Arrange — a cap far from the production default (8), proving RunAsync reads THIS
        // instance's MaxAttempts rather than any constant or another instance's value.
        var policy = new BoundedCasRetryPolicy(maxAttempts: 3, jitterSource: () => 0.0);
        var attemptCalls = 0;

        // Act
        var exception = await Record.ExceptionAsync(() => BoundedCasRetry.RunAsync(
            policy,
            attempt =>
            {
                attemptCalls++;
                return Task.FromResult(false);
            },
            exhaustedException: () => new MarkerException(),
            CancellationToken.None));

        // Assert
        Assert.IsType<MarkerException>(exception);
        Assert.Equal(3, attemptCalls);
    }

    /// <summary>
    /// The one dodge <see cref="Func{Exception}"/> does not rule out at compile time: a factory
    /// that compiles and runs but hands back <see langword="null"/> (<c>() => null!</c>).
    /// <see cref="BoundedCasRetry.RunAsync"/> guards this explicitly rather than letting a bare
    /// <see langword="null"/> propagate as a confusing <c>throw null;</c>/<see cref="NullReferenceException"/> —
    /// still a real, loud throw either way, never a silent return.
    /// </summary>
    [Fact]
    public async Task RunAsync_ShouldThrowAnExplicitException_WhenExhaustedExceptionReturnsNull()
    {
        // Arrange — none

        // Act
        var exception = await Record.ExceptionAsync(() => BoundedCasRetry.RunAsync(
            FastPolicy(),
            _ => Task.FromResult(false),
            exhaustedException: () => null!,
            CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task RunAsync_ShouldPropagateCancellation_WhileWaitingBetweenAttempts()
    {
        // Arrange — CONVENTIONS.md-adjacent guarantee the GL-76 report calls out explicitly:
        // cancellation must reach the loop even though it never touches Mongo here. A token
        // already cancelled makes this deterministic rather than racing a real delay.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var exception = await Record.ExceptionAsync(() => BoundedCasRetry.RunAsync(
            FastPolicy(),
            _ => Task.FromResult(false),
            exhaustedException: () => new InvalidOperationException("should not exhaust"),
            cts.Token));

        // Assert
        Assert.IsType<TaskCanceledException>(exception);
    }

    private sealed class MarkerException : Exception;
}
