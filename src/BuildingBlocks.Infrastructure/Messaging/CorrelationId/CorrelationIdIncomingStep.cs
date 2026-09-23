using Microsoft.Extensions.Logging;
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
///
/// GL-45: also pushes the same id onto <c>logger</c>'s own scope stack
/// (<see cref="CorrelationIdLogging.BeginScope"/>) and writes one log line naming it — the
/// "structured logs" half of ARCHITECTURE.md "Cross-cutting concerns", applied once here rather
/// than in every handler, exactly like the accessor scope above. The explicit log line exists
/// because <c>BuildingBlocks.Testing</c>'s <c>LogCapture</c> records only the formatted message,
/// not scope properties (GL-117) — a real structured sink sees the id both ways, but a test
/// asserting this hop needs it in the message text too.
/// </remarks>
internal sealed class CorrelationIdIncomingStep(
    ICorrelationIdAccessor accessor,
    ILogger<CorrelationIdIncomingStep> logger) : IIncomingStep
{
    public async Task Process(IncomingStepContext context, Func<Task> next)
    {
        var headers = context.Load<TransportMessage>().Headers;
        headers.TryGetValue(Headers.CorrelationId, out var correlationId);
        headers.TryGetValue(Headers.Type, out var messageType);

        using (accessor.BeginScope(correlationId))
        using (CorrelationIdLogging.BeginScope(logger, correlationId))
        {
            logger.LogInformation(
                "Handling {MessageType} with correlation id {CorrelationId}.",
                messageType ?? "message", correlationId);
            await next();
        }
    }
}
