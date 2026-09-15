using Rebus.Messages;
using Rebus.Pipeline;

namespace BuildingBlocks.Messaging.CorrelationId;

/// <summary>
/// Stamps the ambient correlation ID onto an outgoing message that doesn't already carry one —
/// the case of a *new* conversation started outside of any Rebus handler, e.g. the Gateway
/// issuing <c>bus.Send()</c> from a gRPC call. Rebus's own correlation-ID flowing only covers
/// messages sent while already handling one; this step is what lets a gRPC call's correlation ID
/// become the seed for the Rebus message header it triggers (ARCHITECTURE.md "Cross-cutting concerns").
/// </summary>
/// <remarks>
/// Registered to run before Rebus assigns its own default headers, so a value already present
/// here is what Rebus flows onward — it never overwrites a header that's already set.
/// </remarks>
internal sealed class CorrelationIdOutgoingStep(ICorrelationIdAccessor accessor) : IOutgoingStep
{
    public async Task Process(OutgoingStepContext context, Func<Task> next)
    {
        var correlationId = accessor.CorrelationId;
        if (!string.IsNullOrEmpty(correlationId))
        {
            var message = context.Load<Message>();
            message.Headers.TryAdd(Headers.CorrelationId, correlationId);
        }

        await next();
    }
}
