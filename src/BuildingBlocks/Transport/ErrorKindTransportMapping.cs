using BuildingBlocks.Results;

namespace BuildingBlocks.Transport;

/// <summary>
/// The one <see cref="ErrorKind"/> -> gRPC status / GraphQL <c>extensions.code</c> mapping table
/// CONVENTIONS.md "Errors" asks for, used by every service so a failed use case reaches the SPA as the
/// same shape regardless of which service raised it or which surface (grpc-web, GraphQL) carried
/// it.
/// </summary>
/// <remarks>
/// Lives in BCL-only <c>BuildingBlocks</c>, not <c>BuildingBlocks.Infrastructure</c>: one table,
/// used by all four services, returning a plain <see cref="GrpcStatusCode"/> value and a plain
/// string rather than a <c>Grpc.Core</c> or <c>HotChocolate</c> type. The Gateway's grpc-web and
/// GraphQL adapters, in Infrastructure/Host, are the only things that convert
/// <see cref="GrpcStatusCode"/> into an actual <c>Grpc.Core.Status</c> or set
/// <c>extensions.code</c> on a GraphQL error.
/// </remarks>
public static class ErrorKindTransportMapping
{
    /// <summary>
    /// Maps an <see cref="ErrorKind"/> to its gRPC status. Every member of the enum must have an
    /// explicit case here — the default arm throws rather than falling through to
    /// <see cref="GrpcStatusCode.Internal"/>, so a new <see cref="ErrorKind"/> member added
    /// without a mapping fails loudly (a thrown exception a caller notices, and a red
    /// <c>ErrorKindTransportMappingTests</c>) instead of silently becoming a generic 500.
    /// </summary>
    public static GrpcStatusCode ToGrpcStatus(this ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => GrpcStatusCode.InvalidArgument,
        ErrorKind.NotFound => GrpcStatusCode.NotFound,
        ErrorKind.Conflict => GrpcStatusCode.Aborted,
        ErrorKind.Forbidden => GrpcStatusCode.PermissionDenied,
        ErrorKind.Unauthenticated => GrpcStatusCode.Unauthenticated,
        ErrorKind.Unavailable => GrpcStatusCode.Unavailable,
        _ => throw new NotSupportedException(
            $"ErrorKind.{kind} has no gRPC status mapping. Add one to " +
            $"{nameof(ErrorKindTransportMapping)}.{nameof(ToGrpcStatus)} — CONVENTIONS.md \"Errors\" " +
            "requires every ErrorKind member to map to a gRPC status, not fall through to a " +
            "generic one."),
    };

    /// <summary>
    /// Maps an <see cref="ErrorKind"/> to its GraphQL <c>extensions.code</c> value. Same
    /// exhaustiveness guarantee as <see cref="ToGrpcStatus"/>: an unmapped member throws instead
    /// of defaulting.
    /// </summary>
    public static string ToGraphQlCode(this ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => "BAD_USER_INPUT",
        ErrorKind.NotFound => "NOT_FOUND",
        ErrorKind.Conflict => "CONFLICT",
        ErrorKind.Forbidden => "FORBIDDEN",
        ErrorKind.Unauthenticated => "UNAUTHENTICATED",
        ErrorKind.Unavailable => "UNAVAILABLE",
        _ => throw new NotSupportedException(
            $"ErrorKind.{kind} has no GraphQL extensions.code mapping. Add one to " +
            $"{nameof(ErrorKindTransportMapping)}.{nameof(ToGraphQlCode)} — CONVENTIONS.md \"Errors\" " +
            "requires every ErrorKind member to map to a code, not fall through to a generic " +
            "one."),
    };
}
