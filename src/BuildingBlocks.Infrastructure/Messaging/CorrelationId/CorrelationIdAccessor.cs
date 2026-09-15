namespace BuildingBlocks.Messaging.CorrelationId;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed implementation of <see cref="ICorrelationIdAccessor"/>.
/// Register as a singleton — the ambient value flows with the logical call context, not with
/// "the instance", so one shared instance is correct even though the value it reports differs
/// per in-flight message.
/// </summary>
public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly AsyncLocal<string?> _current = new();

    public string? CorrelationId => _current.Value;

    /// <inheritdoc />
    public IDisposable BeginScope(string? correlationId)
    {
        var previous = _current.Value;
        _current.Value = correlationId;
        return new Scope(this, previous);
    }

    private sealed class Scope(CorrelationIdAccessor owner, string? previous) : IDisposable
    {
        public void Dispose() => owner._current.Value = previous;
    }
}
