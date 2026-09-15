namespace BuildingBlocks.Persistence;

/// <summary>
/// The retry/backoff arithmetic behind a bounded compare-and-set loop (GL-76), pulled out as its
/// own ordinary type — not because the loop that consults it needed to be more general, but so
/// the arithmetic itself can be pinned by a plain unit test with no Mongo and no mock, just
/// attempt numbers in and decisions out. <see cref="BoundedCasRetry"/> is what actually consults
/// this while running the loop.
/// </summary>
/// <remarks>
/// <para>
/// <b>Moved here from <c>Gateway.Infrastructure.GiftLists.Persistence</c> by GL-79</b> — see
/// <see cref="BoundedCasRetry"/>'s own doc comment for why the pair belongs in
/// <c>BuildingBlocks</c> rather than under one service's aggregate folder, and for the visibility
/// change (<see langword="internal"/> to <see langword="public"/>) that came with it.
/// <b>Renamed from <c>ProjectionCasRetryPolicy</c> in the same ticket's review (S3):</b> this type
/// "knows nothing about Mongo, documents, or projections" (<see cref="BoundedCasRetry"/>'s own
/// words) and CONVENTIONS.md "Naming" reserves "Projection" for read-model types — a name check away
/// from <c>ProjectionWriteProbe.DeclaresAProjectionType</c> mistaking this file for one the moment
/// it gained an unrelated Mongo mention. Renamed while there was exactly one consumer to update.
/// </para>
/// <para>
/// <b><see cref="MaxAttempts"/> is a per-instance parameter, not a shared constant (GL-79 review,
/// S1).</b> The first cut of this move left it a <see langword="const"/> and told Reservations'
/// author, in prose, to "re-derive their own cap" — advice the API gave them no way to act on:
/// a <see langword="const"/> cannot be overridden, only read, edited (changing Gateway's behaviour
/// too) or forked (the second-wrong-copy this ticket exists to prevent, recreated one level up).
/// A <see langword="const"/> also inlines into a *consumer's* IL at compile time — invisible while
/// this type was <see langword="internal"/> to Gateway alone, but live the moment GL-25 packs
/// <c>BuildingBlocks</c> as a NuGet package: a service built against 1.0 would keep the literal 8
/// baked in even after 1.1 changed it. Now every caller sites its own
/// <paramref name="maxAttempts"/> instead of asking the type to know, in advance, what every
/// consumer's concurrency profile requires; 8 lives on only as the default, justified below for
/// Gateway specifically. Reservations' own author overrides it, deliberately, at the call site —
/// no edit to this file required.
/// </para>
/// <para>
/// The default, 8, is derived from Gateway's own topology, not a law of nature. Rebus's default
/// <c>MaxParallelism</c> is 5 (unset by <c>RebusConfigurationExtensions</c> — the same default
/// GiftLists' own <c>AddGiftItemConcurrencyTests</c> documents for its service), spread across
/// Gateway's <c>GiftListProjectionRepository</c> and its five <c>Apply*</c> handler types, so 5
/// concurrent writers to the SAME list is ordinary contention under normal load there, not a
/// pathological burst. In the worst legitimate interleaving, the one unluckiest of those 5 loses
/// the CAS to each of the other 4 in turn before it is the last one standing — 4 retries, 5
/// attempts total. A cap at or below 5 would therefore fail that writer's attempt spuriously under
/// perfectly normal load, not just under an actual burst — the thing GL-76 wants to distinguish. 8
/// gives 3 attempts of headroom above that proven floor: one for the insert-duplicate-key fallback
/// path (a stub race that consumes an attempt without advancing the version — see
/// <c>GiftListProjectionRepository</c>'s own comment on that branch) and two for jitter/scheduling
/// meaning the 5 racers do not perfectly interleave in the worst-case order every time. Above this,
/// only a genuinely sustained, pathological burst — many more than 5 concurrent writers to one
/// list, or the same 5 retrying in a tight loop — reaches exhaustion. Reservations' own projection
/// (GL-35/36/37) will have its own handler count and may configure <c>MaxParallelism</c>
/// differently, so its author must redo this arithmetic for their own topology and pass the result
/// explicitly — not assume 8 already accounts for a topology it has never seen. This default is
/// pinned by <c>BoundedCasRetryPolicyTests</c>'s own default-value test (GL-79 review, S2), so it
/// cannot silently drift out from under this derivation without a test failing.
/// </para>
/// <para>
/// <see cref="BaseDelayFor"/> doubles from a 20ms starting point, capped at 320ms — uncapped
/// doubling would reach 20ms * 2^6 = 1.28s on the last of 7 possible retries, far longer than a
/// projection write is worth blocking a Rebus worker for.
/// </para>
/// <para>
/// <see cref="DelayFor"/> applies full jitter (the base delay times a factor in [0.5, 1.5)) —
/// without it, the ordinary contention case (5 same-typed handlers racing one document) would
/// retry in lockstep, a self-inflicted thundering herd against the document that is already the
/// bottleneck. The jitter SOURCE is injectable specifically so a test can pin the exact resulting
/// value rather than only its range — an ordinary delegate, not a mocking framework.
/// </para>
/// </remarks>
public sealed class BoundedCasRetryPolicy
{
    /// <summary>The default cap, 8 — Gateway's own value; see this type's own remarks for the derivation and why a new caller should re-derive rather than inherit it.</summary>
    public const int DefaultMaxAttempts = 8;

    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMilliseconds(320);

    private readonly Func<double> _jitterSource;

    /// <param name="maxAttempts">
    /// The cap on attempts (GL-79 review, S1 — an instance parameter, not a shared constant, so
    /// each caller states its own concurrency profile instead of inheriting Gateway's). Defaults
    /// to <see cref="DefaultMaxAttempts"/>; must be at least 1.
    /// </param>
    /// <param name="jitterSource">
    /// Returns a value in [0, 1); the jitter factor applied in <see cref="DelayFor"/> is
    /// 0.5 + that value. Defaults to <see cref="Random.Shared"/>'s own <c>NextDouble</c>.
    /// </param>
    public BoundedCasRetryPolicy(int maxAttempts = DefaultMaxAttempts, Func<double>? jitterSource = null)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts), maxAttempts, "maxAttempts must be at least 1.");
        }

        MaxAttempts = maxAttempts;
        _jitterSource = jitterSource ?? (() => Random.Shared.NextDouble());
    }

    /// <summary>
    /// The cap on attempts for this instance — see the constructor's own <c>maxAttempts</c>
    /// parameter doc and this type's remarks for why it is per-instance rather than shared.
    /// </summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// <see langword="true"/> while another attempt is still allowed. <paramref name="attempt"/>
    /// is 1-based — the attempt that just failed.
    /// </summary>
    public bool CanRetry(int attempt) => attempt < MaxAttempts;

    /// <summary>
    /// The backoff before retrying after <paramref name="attempt"/>, BEFORE jitter — exposed
    /// separately from <see cref="DelayFor"/> so the doubling/ceiling arithmetic itself can be
    /// pinned exactly, independent of randomness. Static: the doubling/ceiling shape has nothing
    /// to do with any one instance's <see cref="MaxAttempts"/> or jitter source.
    /// </summary>
    public static TimeSpan BaseDelayFor(int attempt)
    {
        var exponential = BaseRetryDelay * Math.Pow(2, attempt - 1);
        return exponential < MaxRetryDelay ? exponential : MaxRetryDelay;
    }

    /// <summary>Full jitter applied to <see cref="BaseDelayFor"/> — see this type's own remarks for why.</summary>
    public TimeSpan DelayFor(int attempt) => BaseDelayFor(attempt) * (0.5 + _jitterSource());
}
