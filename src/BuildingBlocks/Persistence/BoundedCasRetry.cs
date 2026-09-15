namespace BuildingBlocks.Persistence;

/// <summary>
/// The attempt-count/backoff/exhaustion SHAPE behind a bounded compare-and-set retry loop
/// (GL-76), pulled out of Gateway's <c>GiftListProjectionRepository.ApplyAsync</c> so that shape
/// could be pinned by a plain unit test with a trivial always-fails delegate — no Mongo, no mock.
/// This type knows nothing about Mongo, documents, or projections; <paramref name="attemptAsync"/>/
/// <c>exhaustedException</c> below are where all of that lives — see <see cref="RunAsync"/>'s own
/// doc comment.
///
/// <para>
/// Moved here from <c>Gateway.Infrastructure.GiftLists.Persistence</c> by GL-79, once GL-76's own
/// remark that this is "domain-agnostic and BCL-only" stopped being a description of what the type
/// happened to look like and started being a reason it should not go on living under a folder
/// named after one aggregate in one service: Reservations gets its own projection in Phase 4
/// (GL-35/36/37), CONVENTIONS.md "Messaging" tells whoever writes it to bound its own CAS retry the same
/// way, and the nearest existing implementation was filed under Gateway's <c>GiftLists</c> folder
/// — reachable only by a cross-service reach into another service's <c>Infrastructure</c> project,
/// which CONVENTIONS.md "Project reference graph" forbids outright (Infrastructure may reference another service's
/// <c>*.Contracts</c>, never its <c>Infrastructure</c>). The <c>BuildingBlocks</c> project is the
/// one place both Gateway and Reservations can already reach (CONVENTIONS.md "Project reference graph"'s reference
/// graph), and this type was already BCL-only, so the move costs nothing today.
/// </para>
///
/// <para>
/// <b>Visibility (GL-79):</b> <see langword="public"/>, not <see langword="internal"/> — reversing
/// GL-76's own choice, deliberately. <see langword="internal"/> was correct while
/// <c>Gateway.Infrastructure</c> was both the only author and the only possible caller: nothing
/// outside that assembly could reach it, so nothing outside it needed to. Once the type lives in
/// <c>BuildingBlocks</c>, "outside the assembly" is exactly what every consumer — Gateway's
/// own <c>GiftListProjectionRepository</c> included — now is; <see langword="internal"/> here
/// would not merely fail to help Gateway keep a secret, it would stop Gateway compiling at all.
/// The fix is not an <c>InternalsVisibleTo</c> grant naming every present and future consumer (a
/// friend-assembly list that Reservations' author would have to remember to extend before this
/// type could help them, exactly the discoverability failure GL-79 exists to close) — it is
/// <see langword="public"/>, matching every other type this project already exports for
/// cross-assembly consumption (<c>Error</c>, <c>Result</c>, <c>ReplyFault</c>,
/// <c>ErrorKindTransportMapping</c>: none of them is <see langword="internal"/>). CONVENTIONS.md's
/// "internal unless genuinely public" is not being set aside here — a type meant to be called from
/// another assembly by design, which is the entire reason for this move, is what "genuinely
/// public" means.
/// </para>
/// </summary>
public static class BoundedCasRetry
{
    /// <summary>
    /// Calls <paramref name="attemptAsync"/> with a 1-based attempt number, up to
    /// <paramref name="policy"/>'s own <see cref="BoundedCasRetryPolicy.MaxAttempts"/> times,
    /// waiting per <paramref name="policy"/> between attempts. <paramref name="attemptAsync"/> returns
    /// <see langword="true"/> once there is nothing further to do (a successful write, or a
    /// mutation that decided nothing needed to change) and <see langword="false"/> to mean "lost
    /// the race, try again".
    /// </summary>
    /// <param name="exhaustedException">
    /// Produces the exception <see cref="RunAsync"/> itself throws once every attempt has
    /// reported <see langword="false"/>. Deliberately a <see cref="Func{Exception}"/> that
    /// <see cref="RunAsync"/> throws, rather than an <c>Action</c> the caller was trusted to
    /// throw from (review, Batch 16 round 3): an <c>Action</c> may legally do nothing, which
    /// makes "exhausted, yet returns normally without having written" — the silent data loss
    /// this whole cap exists to avoid — a state the type system allows and every caller must then
    /// be separately tested against. A <see cref="Func{Exception}"/> that <see cref="RunAsync"/>
    /// itself throws removes that state from the caller's reach entirely: <see cref="RunAsync"/>
    /// either succeeds or throws, for any caller, present or future. A <see langword="null"/>
    /// result is the one remaining dodge (<c>() => null!</c> compiles), so it is guarded below
    /// rather than trusted.
    /// </param>
    public static async Task RunAsync(
        BoundedCasRetryPolicy policy,
        Func<int, Task<bool>> attemptAsync,
        Func<Exception> exhaustedException,
        CancellationToken cancellationToken)
    {
        // GL-79 review (S1): reads policy.MaxAttempts/policy.CanRetry, not a static/const on the
        // policy type — each caller's own instance states its own cap, so this loop is bounded by
        // whatever concurrency profile the CALLER derived, never a value shared across services.
        for (var attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            if (await attemptAsync(attempt))
            {
                return;
            }

            // Skipped on the last attempt — nothing left to wait for once the cap is about to be
            // reached, and waiting there would only delay the throw below.
            if (policy.CanRetry(attempt))
            {
                await Task.Delay(policy.DelayFor(attempt), cancellationToken);
            }
        }

        throw exhaustedException() ?? new InvalidOperationException(
            "BoundedCasRetry.RunAsync's exhaustedException factory returned null after every " +
            "attempt failed. That is the one dodge Func<Exception> does not rule out at compile " +
            "time (Action did not rule out anything) — every caller must produce a REAL exception " +
            "once attempts are exhausted, never null, because returning from here without having " +
            "thrown would still be the silent data loss this type exists to prevent.");
    }
}
