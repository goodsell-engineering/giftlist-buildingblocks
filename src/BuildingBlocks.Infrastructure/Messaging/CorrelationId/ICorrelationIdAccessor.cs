namespace BuildingBlocks.Messaging.CorrelationId;

/// <summary>
/// Ambient access to "the correlation ID of whatever is currently being handled" — the thing
/// that lets one user action be traced end to end (gRPC call → Rebus message header → published
/// event, ARCHITECTURE.md "Cross-cutting concerns") without threading an extra parameter through every method.
/// </summary>
/// <remarks>
/// Read by <see cref="CorrelationIdOutgoingStep"/> to stamp new conversations, and available for
/// a Host to feed into its logging pipeline (e.g. a Serilog enricher) so every log line carries
/// it without per-handler code.
/// </remarks>
public interface ICorrelationIdAccessor
{
    /// <summary>
    /// The current correlation ID, or <see langword="null"/> outside of any tracked scope (e.g.
    /// before the first inbound message/request of a process has been handled).
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    /// Sets the ambient correlation ID for the duration of the returned scope, restoring the
    /// previous value on dispose. On the interface (not just the concrete accessor) so that
    /// anything starting a new unit of work outside of Rebus — a gRPC interceptor, say — can
    /// seed it without a hard dependency on the concrete implementation.
    /// </summary>
    IDisposable BeginScope(string? correlationId);
}
