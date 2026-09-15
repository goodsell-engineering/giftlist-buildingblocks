using Rebus.Messages;
using Rebus.Pipeline;

namespace BuildingBlocks.Messaging.CorrelationId;

/// <summary>
/// Reads the incoming message's <see cref="Headers.CorrelationId"/> header into the ambient
/// <see cref="ICorrelationIdAccessor"/> for the duration of handling, so that logging and any
/// further messages sent while handling can see it without every handler doing this itself
/// (ARCHITECTURE.md "Cross-cutting concerns" — "propagate it there once rather than in every handler").
/// </summary>
/// <remarks>
/// Rebus already flows a correlation ID between messages sent from within a handler
/// (<c>FlowCorrelationIdStep</c>); this step exists for the concerns Rebus's own step doesn't
/// cover — making the value available outside message headers (e.g. to a Serilog enricher) and
/// giving <see cref="CorrelationIdOutgoingStep"/> something to stamp onto a conversation that is
/// *starting* here rather than being replied to.
/// </remarks>
internal sealed class CorrelationIdIncomingStep(ICorrelationIdAccessor accessor) : IIncomingStep
{
    public async Task Process(IncomingStepContext context, Func<Task> next)
    {
        var headers = context.Load<TransportMessage>().Headers;
        headers.TryGetValue(Headers.CorrelationId, out var correlationId);

        using (accessor.BeginScope(correlationId))
        {
            await next();
        }
    }
}
