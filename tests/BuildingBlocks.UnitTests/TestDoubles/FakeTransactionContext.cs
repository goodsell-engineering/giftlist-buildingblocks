using System.Collections.Concurrent;
using Rebus.Transport;

namespace BuildingBlocks.UnitTests.TestDoubles;

/// <summary>
/// The minimal <see cref="ITransactionContext"/> needed to construct a Rebus step context in a
/// test — none of the commit/rollback/ack plumbing is exercised by the pipeline steps under
/// test, so every callback is a no-op.
/// </summary>
internal sealed class FakeTransactionContext : ITransactionContext
{
    public ConcurrentDictionary<string, object> Items { get; } = new();

    public void OnCommit(Func<ITransactionContext, Task> callback)
    {
    }

    public void OnRollback(Func<ITransactionContext, Task> callback)
    {
    }

    public void OnAck(Func<ITransactionContext, Task> callback)
    {
    }

    public void OnNack(Func<ITransactionContext, Task> callback)
    {
    }

    public void OnDisposed(Action<ITransactionContext> callback)
    {
    }

    public void SetResult(bool commit, bool ack)
    {
    }

    public void Dispose()
    {
    }
}
