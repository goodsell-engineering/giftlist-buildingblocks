namespace BuildingBlocks.Messaging.RequestReply;

/// <summary>
/// One in-flight request waiting for its reply: the <see cref="TaskCompletionSource{TResult}"/>
/// that bridges a Rebus reply (arriving on a Rebus worker thread) back to the gRPC call blocked
/// on it (ARCHITECTURE.md "Command → event flow").
/// </summary>
/// <remarks>
/// Owned by <see cref="PendingRequestRegistry"/>; obtained from
/// <see cref="PendingRequestRegistry.Register"/> and <b>always</b> disposed by the waiter, which
/// is what unregisters it. Dispose is the only removal path — a reply that never arrives would
/// otherwise leak an entry per timed-out request.
/// </remarks>
internal sealed class PendingRequest : IDisposable
{
    // RunContinuationsAsynchronously is load-bearing, not a micro-optimisation: without it the
    // waiter's continuation — the rest of a gRPC call, JWT signing and all — runs inline on the
    // Rebus worker thread inside the message's transaction context, delaying the ack and
    // coupling message throughput to request latency.
    private readonly TaskCompletionSource<object> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly PendingRequestRegistry _registry;

    internal PendingRequest(string requestId, PendingRequestRegistry registry)
    {
        RequestId = requestId;
        _registry = registry;
    }

    /// <summary>The request message's id, which the reply carries back as its in-reply-to header.</summary>
    public string RequestId { get; }

    /// <summary>Completes with the deserialized reply message body once one arrives.</summary>
    public Task<object> Reply => _completion.Task;

    /// <summary>
    /// Hands the reply to the waiter. Returns <see langword="false"/> if this request was already
    /// completed — a redelivery of the same reply, which is normal under Rebus's at-least-once
    /// delivery and not an error.
    /// </summary>
    public bool TryComplete(object reply) => _completion.TrySetResult(reply);

    public void Dispose() => _registry.Remove(RequestId);
}
