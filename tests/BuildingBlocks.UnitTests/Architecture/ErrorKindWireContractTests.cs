using BuildingBlocks.Results;

namespace ArchitectureTests;

/// <summary>
/// CONVENTIONS.md "Errors"'s <see cref="ErrorKind"/> stopped being an internal classification the day
/// business failures started crossing service boundaries: a failed use case replies with a
/// <c>ReplyFault</c>, which carries the kind as its member name, so these names are a wire
/// contract between two independently deployed services [AT]. Renaming a member is then exactly
/// as breaking as renaming an integration event — and just as silent, because both sides still
/// compile and the mismatch only shows up as a fault landing on the far side as the
/// forward-compatibility fallback instead of what was actually sent.
///
/// Lives here rather than in the arch-test set synced across the five repos
/// (<c>architecture-tests.sha256</c>): the enum exists only in BuildingBlocks, so this is a
/// BuildingBlocks-local rule, same as <see cref="ErrorKindTransportMappingTests"/>.
/// </summary>
public class ErrorKindWireContractTests
{
    [Fact]
    public void ErrorKind_ShouldKeepEveryMemberNameAndValue_BecauseTheyCrossTheWire()
    {
        // Arrange — the pinned contract. Adding a kind means appending one entry here and
        // extending the CONVENTIONS.md "Errors" transport mapping (GL-53) to cover it. Editing an existing entry
        // means breaking every deployed service that already speaks it: don't. Values are pinned
        // alongside names because the names are only the wire form for as long as nothing
        // serializes the enum itself — one serializer setting away from the ordinals mattering.
        const string expected =
            "Validation=0, NotFound=1, Conflict=2, Forbidden=3, Unavailable=4, Unauthenticated=5";

        // Act
        var actual = string.Join(", ", Enum.GetValues<ErrorKind>().Select(kind => $"{kind}={(int)kind}"));

        // Assert
        Assert.True(expected == actual,
            "ErrorKind's members are a wire contract (they travel as ReplyFault.Kind), and this " +
            $"set has changed.{Environment.NewLine}Pinned:  {expected}{Environment.NewLine}Actual:  {actual}" +
            $"{Environment.NewLine}Append new members and update the pinned string; never rename " +
            "or renumber an existing one.");
    }
}
