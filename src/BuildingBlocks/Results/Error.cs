namespace BuildingBlocks.Results;

/// <summary>
/// An expected failure outcome of a use case. Not an exception — "email already taken" and
/// "already reserved" are ordinary results, not thrown.
/// </summary>
/// <param name="Code">
/// A stable, machine-readable identifier (e.g. <c>"giftlist.expired"</c>). Safe to switch on and
/// to key a UI message off. Never reworded once shipped — add a new code instead.
/// </param>
/// <param name="Message">
/// A human-readable message for logs and diagnostics. Must never contain PII.
/// </param>
/// <param name="Kind">The category used to map this error onto a transport status.</param>
public sealed record Error(string Code, string Message, ErrorKind Kind);
