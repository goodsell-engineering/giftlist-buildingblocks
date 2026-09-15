using BuildingBlocks.Results;

namespace BuildingBlocks.Messaging.RequestReply;

/// <summary>
/// The failures the request/reply bridge itself can produce, as <see cref="Error"/> values
/// (CONVENTIONS.md "Errors") — a reply that never arrives is an expected outcome with a UX fallback,
/// not an exception. Broker failures on the way out are not here: those are infrastructure
/// faults and are left to throw.
/// </summary>
/// <remarks>
/// In BCL-only BuildingBlocks, not beside the bridge in BuildingBlocks.Infrastructure, even
/// though the bridge is the only thing that raises it. The point of a stable code is that a
/// caller can branch on it — and the caller that needs to (to show ARCHITECTURE.md "Command → event flow"'s "still
/// working on it" fallback) is Application-ring, which CONVENTIONS.md "Project reference graph" forbids from
/// referencing BuildingBlocks.Infrastructure at all.
/// </remarks>
public static class RequestReplyErrors
{
    /// <summary>
    /// Stable code for "we gave up waiting". Callers switch on this to show the "still working
    /// on it" fallback (ARCHITECTURE.md "Command → event flow") rather than a hard error.
    /// </summary>
    public const string ReplyTimeoutCode = "messaging.reply_timeout";

    /// <summary>
    /// <see cref="ErrorKind.Unavailable"/>, so the shared transport mapping (CONVENTIONS.md "Errors")
    /// turns it into gRPC <c>UNAVAILABLE</c> / GraphQL <c>UNAVAILABLE</c>: the work may well
    /// still be happening, which is exactly what "unavailable" means and "not found" or
    /// "aborted" would not.
    /// </summary>
    /// <param name="requestType">The request message type — a type name, never user data.</param>
    public static Error ReplyTimedOut(Type requestType, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        return new Error(
            ReplyTimeoutCode,
            $"No reply to '{requestType.Name}' arrived within {timeout.TotalSeconds:0.###}s.",
            ErrorKind.Unavailable);
    }
}
