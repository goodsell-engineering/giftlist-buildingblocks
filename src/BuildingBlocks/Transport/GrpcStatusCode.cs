namespace BuildingBlocks.Transport;

/// <summary>
/// The canonical gRPC status codes, defined locally rather than referencing <c>Grpc.Core</c> —
/// CONVENTIONS.md "Project reference graph" forbids <c>Grpc.*</c> from being reachable from <c>BuildingBlocks</c> or
/// <c>Application</c>, and the placement note on GL-53 asks for a plain status code value that a
/// Gateway adapter translates, not a type from the gRPC package itself.
/// </summary>
/// <remarks>
/// Numeric values match <c>Grpc.Core.StatusCode</c> exactly (both are restatements of the same
/// gRPC spec), so a Gateway adapter in Infrastructure/Host can cast one to the other —
/// <c>(Grpc.Core.StatusCode)(int)grpcStatusCode</c> — instead of maintaining a second translation
/// table. Member <i>names</i> do not match letter-for-letter (this enum follows C# PascalCase,
/// e.g. <see cref="Ok"/>; <c>Grpc.Core.StatusCode</c> spells the same value <c>OK</c>) — harmless
/// for the numeric cast, but do not rely on the names lining up. Only the subset CONVENTIONS.md
/// "Errors" actually maps to is populated; the rest of the canonical set is included for completeness
/// and for that cast to stay safe if a future mapping needs one of them. The cast itself has no
/// test yet — <c>BuildingBlocks</c> cannot reference <c>Grpc.Core</c> to write one; pin it with a
/// Gateway-side test (referencing <c>Grpc.*</c> is fine there) when the grpc-web adapter lands
/// (GL-18).
/// </remarks>
public enum GrpcStatusCode
{
    Ok = 0,
    Cancelled = 1,
    Unknown = 2,
    InvalidArgument = 3,
    DeadlineExceeded = 4,
    NotFound = 5,
    AlreadyExists = 6,
    PermissionDenied = 7,
    ResourceExhausted = 8,
    FailedPrecondition = 9,
    Aborted = 10,
    OutOfRange = 11,
    Unimplemented = 12,
    Internal = 13,
    Unavailable = 14,
    DataLoss = 15,
    Unauthenticated = 16,
}
