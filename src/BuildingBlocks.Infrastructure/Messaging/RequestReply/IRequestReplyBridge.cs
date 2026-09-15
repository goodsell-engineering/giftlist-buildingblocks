using BuildingBlocks.Results;

namespace BuildingBlocks.Messaging.RequestReply;

/// <summary>
/// Sends a command and waits for the handling service's <c>bus.Reply()</c> — the request/reply
/// half of ARCHITECTURE.md "Messaging", used only where a user is actively waiting on a yes/no (Login,
/// SignUp, ReserveGift). Everything else is fire-and-forget <c>bus.Send</c> plus a subscription
/// once the read model catches up; reach for this only when the caller genuinely cannot proceed
/// without the answer.
/// </summary>
/// <remarks>
/// <para>
/// The responder side needs almost nothing from this library: a handler calls
/// <c>bus.Reply(reply)</c> on success and <c>bus.Reply(ReplyFault.From(error))</c> on failure,
/// and Rebus addresses it to the requester's input queue with the request's message id in the
/// <c>rbs2-in-reply-to</c> header, which is what this bridge matches on.
/// </para>
/// <para>
/// Failure is a <see cref="Result{T}"/>, not an exception, and there is exactly one failure
/// channel: a use case that failed (arriving as a <see cref="ReplyFault"/>) and a reply that
/// never arrived both surface as <c>Result&lt;TReply&gt;.Failure</c> with the originating
/// <see cref="Error"/> — so a caller never has to unpack success-flags out of the reply type as
/// well (CONVENTIONS.md "Errors"). A broker that cannot be reached at all is an infrastructure fault
/// and still throws.
/// </para>
/// </remarks>
public interface IRequestReplyBridge
{
    /// <summary>
    /// Sends <paramref name="request"/> and waits for a <typeparamref name="TReply"/> for the
    /// configured default timeout (<c>Rebus:ReplyTimeoutSeconds</c>, ~5s).
    /// </summary>
    /// <returns>
    /// The reply; the <see cref="Error"/> the handler failed with, if it sent a
    /// <see cref="ReplyFault"/>; or <see cref="RequestReplyErrors.ReplyTimedOut"/> if nothing
    /// arrived in time.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// The caller cancelled — e.g. the gRPC client hung up. The bridge stops waiting; the
    /// command itself has already been sent and will still be handled.
    /// </exception>
    Task<Result<TReply>> SendAndAwaitReply<TReply>(object request, CancellationToken cancellationToken = default)
        where TReply : class;

    /// <inheritdoc cref="SendAndAwaitReply{TReply}(object, CancellationToken)"/>
    /// <param name="timeout">
    /// Overrides the configured default for this one call. Must be positive.
    /// </param>
    Task<Result<TReply>> SendAndAwaitReply<TReply>(
        object request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TReply : class;
}
