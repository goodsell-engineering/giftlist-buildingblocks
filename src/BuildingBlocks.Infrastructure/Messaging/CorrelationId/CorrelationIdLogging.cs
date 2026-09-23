using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging.CorrelationId;

/// <summary>
/// The one place the shape of a correlation-id logger scope is decided (GL-45), shared by
/// <see cref="CorrelationIdIncomingStep"/> (the Rebus ingress) and whatever HTTP-facing ingress a
/// Host adds in front of it (a gRPC/GraphQL request establishing the very first correlation id of
/// a conversation) — so both ends push a scope shaped identically, and any
/// <see cref="ILogger"/>-based sink downstream (a Serilog enricher included, were one ever added)
/// sees one structured property, not two differently-named ones
/// (ARCHITECTURE.md "Cross-cutting concerns": "propagate it there once rather than in every handler").
/// </summary>
public static class CorrelationIdLogging
{
    /// <summary>The scope property name every log line's structured correlation id is keyed under.</summary>
    public const string PropertyName = "CorrelationId";

    /// <summary>
    /// Pushes <paramref name="correlationId"/> onto <paramref name="logger"/>'s scope stack, or
    /// returns <see langword="null"/> (a no-op scope) when there is nothing to propagate — an
    /// absent correlation id should not show up as a literal "null" structured property on every
    /// log line emitted outside of any tracked conversation (e.g. before the first message this
    /// process ever handles).
    /// </summary>
    public static IDisposable? BeginScope(ILogger logger, string? correlationId) =>
        string.IsNullOrEmpty(correlationId)
            ? null
            : logger.BeginScope(new Dictionary<string, object?> { [PropertyName] = correlationId });
}
