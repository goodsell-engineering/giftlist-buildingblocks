using BuildingBlocks.Testing;
using Testcontainers.RabbitMq;

namespace BuildingBlocks.IntegrationTests.Fixtures;

/// <summary>
/// One real RabbitMQ container for the whole assembly (CONVENTIONS.md "Testing" — containers start
/// once per assembly, never per test). Isolation between tests comes from every test using its
/// own randomly-named queue(s), not from restarting or purging the broker — see
/// <see cref="Support.TestRebusHost"/> for the per-test cleanup that keeps that cheap.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    /// <summary>
    /// GL-92: without this, the reuse hash of the container below is identical to the one
    /// Identity.IntegrationTests and GiftLists.IntegrationTests were computing (same image, same
    /// builder calls, no distinguishing label), so <c>.WithReuse(true)</c> attaches all three
    /// suites to one broker. This suite's queues are randomly named, which hides the damage in
    /// one direction but not the other: it still shares a broker's memory and connection limits
    /// with whatever service suite is running, and a shared broker is how the GL-18 error-path
    /// flake started. The value is the assembly name's prefix, lower-cased;
    /// <c>SuiteLabelRuleTests</c> in BuildingBlocks.UnitTests derives the same string from the
    /// .csproj and fails if they disagree.
    /// </summary>
    private const string SuiteLabel = "buildingblocks";

    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:3.13-management")
        // Local dev loop reuses a warm container across runs; CI (which sets CI=true, same
        // convention every GitHub Actions runner and this repo's own compose usage follow)
        // always starts clean, matching CONVENTIONS.md "Testing".
        .WithReuse(Environment.GetEnvironmentVariable("CI") != "true")
        .WithLabel("giftlist.suite", SuiteLabel)
        // GL-93: the default wait strategy reads container history rather than probing the live
        // broker, so it is not safe across the restarts a `.WithReuse(true)` container
        // accumulates locally — see ReliableReadiness's doc comment.
        .WithReliableWaitStrategy()
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartReliablyAsync(SuiteLabel);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
