using BuildingBlocks.Results;
using BuildingBlocks.Transport;

namespace ArchitectureTests;

/// <summary>
/// CONVENTIONS.md "Errors"'s <c>ErrorKind</c> -> gRPC status / GraphQL <c>extensions.code</c> mapping
/// table [AT], implemented by <see cref="ErrorKindTransportMapping"/>. This pins CONVENTIONS.md "Errors"'s table
/// verbatim and drives the check off <see cref="Enum.GetValues{TEnum}"/> rather than a fixed
/// list, so a new <see cref="ErrorKind"/> member with no row here — or no case in the production
/// switch — fails this test instead of silently falling through to a generic status.
///
/// GL-53. Was a named, visible <c>Skip</c> until this landed; see git history for the
/// placeholder this replaced.
/// </summary>
public class ErrorKindTransportMappingTests
{
    /// <summary>
    /// CONVENTIONS.md "Errors"'s table, pinned here independently of
    /// <see cref="ErrorKindTransportMapping"/>'s implementation so this test cannot pass by
    /// merely echoing whatever the production switch happens to say.
    /// </summary>
    private static readonly IReadOnlyDictionary<ErrorKind, (GrpcStatusCode GrpcStatus, string GraphQlCode)> Expected =
        new Dictionary<ErrorKind, (GrpcStatusCode, string)>
        {
            [ErrorKind.Validation] = (GrpcStatusCode.InvalidArgument, "BAD_USER_INPUT"),
            [ErrorKind.NotFound] = (GrpcStatusCode.NotFound, "NOT_FOUND"),
            [ErrorKind.Conflict] = (GrpcStatusCode.Aborted, "CONFLICT"),
            [ErrorKind.Forbidden] = (GrpcStatusCode.PermissionDenied, "FORBIDDEN"),
            [ErrorKind.Unauthenticated] = (GrpcStatusCode.Unauthenticated, "UNAUTHENTICATED"),
            [ErrorKind.Unavailable] = (GrpcStatusCode.Unavailable, "UNAVAILABLE"),
        };

    [Fact]
    public void ErrorKind_ShouldHaveAPinnedRow_ForEveryMember()
    {
        // Arrange
        var kinds = Enum.GetValues<ErrorKind>();

        // Act
        var unrowed = kinds.Where(kind => !Expected.ContainsKey(kind)).ToList();

        // Assert
        Assert.True(unrowed.Count == 0,
            $"ErrorKind gained a member with no row in this test's pinned CONVENTIONS.md \"Errors\" table: " +
            $"{string.Join(", ", unrowed)}. Add a row here (and to CONVENTIONS.md \"Errors\" and " +
            $"{nameof(ErrorKindTransportMapping)}) before this can pass.");
    }

    [Fact]
    public void ErrorKind_ShouldMapToGrpcStatusAndGraphQlCode_ForEveryKind()
    {
        // Arrange
        var kinds = Enum.GetValues<ErrorKind>();

        // Act
        var actual = kinds.ToDictionary(
            kind => kind,
            kind => (GrpcStatus: kind.ToGrpcStatus(), GraphQlCode: kind.ToGraphQlCode()));

        // Assert
        foreach (var kind in kinds)
        {
            Assert.True(Expected.TryGetValue(kind, out var expected),
                $"{kind} has no expected row — see " +
                $"{nameof(ErrorKind_ShouldHaveAPinnedRow_ForEveryMember)} for the real failure.");
            Assert.Equal(expected, actual[kind]);
        }
    }

    /// <summary>
    /// Not an independent check of the production mapping — <see cref="ErrorKind_ShouldMapToGrpcStatusAndGraphQlCode_ForEveryKind"/>
    /// already does that, row for row, against <see cref="Expected"/>. This guards
    /// <see cref="Expected"/> itself: nothing stops a future engineer from "fixing" a test
    /// failure by adding a plausible-looking <c>(Internal, "INTERNAL_SERVER_ERROR")</c> row to
    /// both <see cref="Expected"/> and the production switch instead of the row CONVENTIONS.md
    /// "Errors" actually specifies — which would make the two agree with each other while both silently
    /// disagreeing with CONVENTIONS.md "Errors". This test would catch that specific failure mode; it would not catch
    /// an unrelated wrong-but-not-generic row, which is <see cref="Expected"/> being wrong in a
    /// way only a manual re-read against CONVENTIONS.md "Errors" catches.
    /// </summary>
    [Fact]
    public void ToGrpcStatus_And_ToGraphQlCode_ShouldNotSilentlyDefaultToInternalOrGeneric()
    {
        // Arrange
        var kinds = Enum.GetValues<ErrorKind>();
        var bannedGrpcFallback = GrpcStatusCode.Internal;
        var bannedGraphQlFallback = "INTERNAL_SERVER_ERROR";

        // Act
        var offenders = kinds
            .Where(kind => kind.ToGrpcStatus() == bannedGrpcFallback
                           || kind.ToGraphQlCode() == bannedGraphQlFallback)
            .ToList();

        // Assert
        Assert.True(offenders.Count == 0,
            $"These ErrorKind members mapped to the generic fallback status, which CONVENTIONS.md \"Errors\" never " +
            $"assigns to any of them: {string.Join(", ", offenders)}.");
    }
}
