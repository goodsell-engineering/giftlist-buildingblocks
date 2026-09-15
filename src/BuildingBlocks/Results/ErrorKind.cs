namespace BuildingBlocks.Results;

/// <summary>
/// Classifies an <see cref="Error"/> so that transport adapters (gRPC, GraphQL, ...) can map it
/// to the right wire-level status without every service inventing its own mapping.
/// </summary>
/// <remarks>
/// These member names are a wire contract, not just source: an error crossing a service boundary
/// travels as <c>BuildingBlocks.Messaging.RequestReply.ReplyFault</c>, which carries the kind as
/// its member name. Append new members; never rename or renumber an existing one.
/// <c>ErrorKindWireContractTests</c> pins the whole set [AT].
/// </remarks>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unavailable,

    /// <summary>
    /// The caller is not (or no longer) authenticated — bad credentials, a missing or expired
    /// token. Distinct from <see cref="Forbidden"/>, which means "we know who you are and the
    /// answer is still no": the two map to different transport statuses (401 vs 403), and a
    /// client that cannot tell them apart cannot decide between "log in again" and "you may not
    /// do this". Appended, not slotted in beside Forbidden, to keep existing values stable.
    /// </summary>
    Unauthenticated,
}
