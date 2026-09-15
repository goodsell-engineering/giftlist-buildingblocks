using System.Collections.Concurrent;

namespace BuildingBlocks.Messaging.RequestReply;

/// <summary>
/// The pending-request registry of ARCHITECTURE.md "Command → event flow": the map from "a request this process
/// sent and is still waiting on" to the <see cref="TaskCompletionSource{TResult}"/> that will
/// release the caller when the reply lands. One singleton per process; the two halves of the
/// bridge meet here and nowhere else — <see cref="RequestReplyBridge"/> registers, and
/// <see cref="PendingReplyIncomingStep"/> completes.
/// </summary>
/// <remarks>
/// Requests are keyed by the request message's own id (<c>rbs2-message-id</c>), which Rebus
/// echoes back on a reply as <c>rbs2-in-reply-to</c> — not by correlation id. A Rebus correlation
/// id identifies a whole conversation and is flowed onto every descendant message, including the
/// integration events the Gateway also consumes to build its read model, so it does not uniquely
/// identify one waiting call; the message id does. The correlation id keeps doing its own job
/// (tracing, ARCHITECTURE.md "Cross-cutting concerns") untouched.
///
/// Known limitation, deliberate for this build: a reply is delivered to the *queue* the request
/// was sent from, so with more than one replica of a service sharing one input queue a reply can
/// land on a replica that has no matching pending request — it is dropped with a warning and the
/// original caller times out. Scaling the Gateway out therefore needs a per-instance reply queue
/// before it needs anything else here.
/// </remarks>
internal sealed class PendingRequestRegistry
{
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new(StringComparer.Ordinal);

    /// <summary>
    /// The number of requests currently awaiting a reply. Internal, for tests only (GL-57) — it
    /// exists purely to make "does <see cref="PendingRequest.Dispose"/> actually unregister,
    /// leaving no leak behind" an assertable fact instead of an inference from "the test didn't
    /// hang".
    /// </summary>
    internal int Count => _pending.Count;

    /// <summary>
    /// Starts waiting for the reply to <paramref name="requestId"/>. Must happen before the
    /// request is sent: a reply can arrive before <c>bus.Send</c>'s own continuation runs.
    /// </summary>
    public PendingRequest Register(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var pending = new PendingRequest(requestId, this);
        if (!_pending.TryAdd(requestId, pending))
        {
            // A duplicate request id means the id generator repeated itself or a caller reused
            // an id — a bug, not an expected outcome, so it throws rather than returning a Result.
            throw new InvalidOperationException(
                $"A request is already pending under id '{requestId}'.");
        }

        return pending;
    }

    /// <summary>
    /// Hands a reply to whoever is waiting for <paramref name="requestId"/>. Returns
    /// <see langword="false"/> when nobody is — the request already timed out, or this is a
    /// redelivery of a reply that was already accepted.
    /// </summary>
    public bool TryComplete(string requestId, object reply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(reply);

        return _pending.TryGetValue(requestId, out var pending) && pending.TryComplete(reply);
    }

    internal void Remove(string requestId) => _pending.TryRemove(requestId, out _);
}
