using Rebus.Logging;
using Rebus.Messages;
using Rebus.Pipeline;

namespace BuildingBlocks.Messaging.RequestReply;

/// <summary>
/// The receiving half of the bridge (ARCHITECTURE.md "Command → event flow"): spots an incoming message that is a
/// reply to something this process sent, and hands it to the waiting caller instead of to a
/// handler. Everything else — commands, integration events — passes straight through untouched.
/// </summary>
/// <remarks>
/// A reply is recognised by Rebus's own <c>rbs2-in-reply-to</c> header, set automatically by
/// <c>bus.Reply()</c>, and matched against <see cref="PendingRequestRegistry"/> by the request's
/// message id. Runs after deserialization (it needs the message body) and before handler
/// activation (there is no handler for a reply — the waiter is not one).
///
/// A <see cref="ReplyFault"/> needs no special case here: it is matched and delivered like any
/// other reply body, and <see cref="RequestReplyBridge"/> — which knows what the caller asked
/// for — turns it into a failed result.
/// </remarks>
internal sealed class PendingReplyIncomingStep(PendingRequestRegistry registry, ILog log) : IIncomingStep
{
    public async Task Process(IncomingStepContext context, Func<Task> next)
    {
        var message = context.Load<Message>();

        if (!message.Headers.TryGetValue(Headers.InReplyTo, out var requestId) ||
            string.IsNullOrWhiteSpace(requestId))
        {
            await next().ConfigureAwait(false);
            return;
        }

        if (registry.TryComplete(requestId, message.Body))
        {
            // Deliberately does not call next(): the reply has been delivered to its caller, and
            // no handler is registered for it. Completing the pipeline here acks the message.
            return;
        }

        // Nobody is waiting: either the caller already timed out (the ~5s fallback doing its job
        // — the common case, and normal), or this is a redelivery of a reply already accepted.
        // Both are expected races, so the message is acked rather than retried into the error
        // queue; a timeout that reliably poisoned a queue would make an ordinary slow request
        // look like a broken system. Logged as a warning because a steady stream of these means
        // something upstream is genuinely too slow.
        log.Warn(
            "Received a reply to request {0} of type {1}, but no caller is waiting for it — it " +
            "timed out, or the reply was already delivered. Discarding.",
            requestId,
            message.Body.GetType().Name);
    }
}
