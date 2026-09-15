using BuildingBlocks.Results;

namespace BuildingBlocks.Messaging.RequestReply;

/// <summary>
/// The reply a request/reply handler sends when its use case failed (ARCHITECTURE.md "Command → event flow"). It
/// is the one way business failure travels back to a waiting caller, so a requester unpacks a
/// single <see cref="Result{T}"/> rather than a success reply, a per-message failure envelope
/// and a timeout — three shapes for one question.
/// </summary>
/// <remarks>
/// <para>
/// Lives in BCL-only BuildingBlocks rather than a <c>*.Contracts</c> package on purpose. Both
/// ends of this message are Infrastructure-ring (a Rebus handler replies; the bridge receives),
/// and CONVENTIONS.md "Project reference graph" forbids Contracts from referencing BuildingBlocks — a Contracts-hosted
/// fault type could therefore not carry an <see cref="Error"/> or an <see cref="ErrorKind"/> at
/// all, which is how the flattened per-reply envelopes it replaces came about.
/// </para>
/// <para>
/// <see cref="Kind"/> is the <see cref="ErrorKind"/> member <i>name</i>, not the enum, so the
/// wire form is stable whether or not a serializer is configured to write enums as strings, and
/// so a service on an older BuildingBlocks can still read a fault from a newer one. Build these
/// with <see cref="From"/> rather than the constructor; the constructor is public only because
/// deserialization needs it.
/// </para>
/// </remarks>
/// <param name="Code">The failing <see cref="Error.Code"/>, e.g. <c>identity.email_taken</c>.</param>
/// <param name="Message">The failing <see cref="Error.Message"/>. Never contains PII.</param>
/// <param name="Kind">The failing <see cref="Error.Kind"/>, as its member name.</param>
public sealed record ReplyFault(string Code, string Message, string Kind)
{
    /// <summary>Wraps a use case's <see cref="Error"/> for the trip back to the caller.</summary>
    public static ReplyFault From(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ReplyFault(error.Code, error.Message, error.Kind.ToString());
    }

    /// <summary>
    /// Reconstructs the <see cref="Error"/> on the requesting side. A <see cref="Kind"/> this
    /// process does not recognise — a member added by a newer service, or wire data that never
    /// named a member at all — reads as <see cref="ErrorKind.Unavailable"/> rather than
    /// throwing: an unmappable failure is still a failure, and the caller degrades to "something
    /// went wrong, try again" instead of turning a handled business outcome into a 500.
    /// </summary>
    /// <remarks>
    /// The <see cref="Enum.IsDefined(Type,object)"/> check matters: <see cref="Enum.TryParse"/>
    /// alone also accepts the string form of the underlying integer (e.g. <c>"42"</c>), defined
    /// member or not, which would let untrusted wire data produce an <see cref="ErrorKind"/> with
    /// no matching member — and every consumer downstream (e.g. the CONVENTIONS.md "Errors" transport
    /// mapping, GL-53) is entitled to assume it never sees one.
    /// </remarks>
    public Error ToError() =>
        new(Code, Message, Enum.TryParse<ErrorKind>(Kind, ignoreCase: false, out var kind)
                            && Enum.IsDefined(kind)
            ? kind
            : ErrorKind.Unavailable);
}
