using BuildingBlocks.Results;
using Rebus.Bus;
using Rebus.Messages;

namespace BuildingBlocks.Messaging.RequestReply;

/// <summary>
/// The requester half of the bridge (ARCHITECTURE.md "Command → event flow"). Registers a waiter, sends the
/// command under a known message id, and blocks the caller's async flow until
/// <see cref="PendingReplyIncomingStep"/> completes it from a Rebus worker thread — or until the
/// timeout turns the wait into a <see cref="RequestReplyErrors.ReplyTimedOut"/> result.
/// </summary>
/// <remarks>
/// This is also where a <see cref="ReplyFault"/> becomes a failed <see cref="Result{T}"/>. The
/// receive step cannot do it: it is not generic, so it has no <c>TReply</c> to fail against, and
/// a fault is an ordinary reply as far as matching a waiter goes. Doing it here keeps the step's
/// job to exactly one thing — deliver the body to whoever is waiting — and puts the knowledge of
/// what the caller asked for in the only place that has it.
/// </remarks>
internal sealed class RequestReplyBridge(
    IBus bus,
    PendingRequestRegistry registry,
    RequestReplyOptions options,
    TimeProvider timeProvider) : IRequestReplyBridge
{
    public Task<Result<TReply>> SendAndAwaitReply<TReply>(
        object request,
        CancellationToken cancellationToken = default)
        where TReply : class =>
        SendAndAwaitReply<TReply>(request, options.ReplyTimeout, cancellationToken);

    public async Task<Result<TReply>> SendAndAwaitReply<TReply>(
        object request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TReply : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        // We choose the request's message id rather than letting Rebus generate one, because it
        // is the key the reply comes back under. Rebus's AssignDefaultHeadersStep only fills in
        // headers that are absent, so an explicitly set id survives to the wire — the same
        // property CorrelationIdOutgoingStep already relies on.
        var requestId = Guid.NewGuid().ToString();

        // Register before sending, never after: the handler can reply, and the reply can be
        // received here, before the await on Send has even resumed. Registering afterwards is
        // the classic version of this bug and it only shows up under load.
        using var pending = registry.Register(requestId);

        await bus.Send(request, new Dictionary<string, string> { [Headers.MessageId] = requestId })
            .ConfigureAwait(false);

        object reply;
        try
        {
            reply = await pending.Reply
                .WaitAsync(timeout, timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Expected outcome, not a fault: the command is still in flight and may yet be
            // handled. The caller shows the "still working on it" fallback.
            return RequestReplyErrors.ReplyTimedOut(request.GetType(), timeout);
        }

        if (reply is ReplyFault fault)
        {
            // The use case ran and failed — "email already taken", "no such gift". An expected
            // outcome, and it arrives through the same Result<T> the timeout above does, which
            // is the whole point of ReplyFault: the caller unpacks one shape, not three.
            return Result<TReply>.Failure(fault.ToError());
        }

        if (reply is not TReply typedReply)
        {
            // A wiring error — this service replied with something the caller does not expect —
            // so it throws rather than returning a Result: no user action can produce it and no
            // UX can recover from it.
            throw new InvalidOperationException(
                $"Expected a reply of type '{typeof(TReply).Name}' or '{nameof(ReplyFault)}' to " +
                $"'{request.GetType().Name}', but received '{reply.GetType().Name}'.");
        }

        return Result<TReply>.Success(typedReply);
    }
}
