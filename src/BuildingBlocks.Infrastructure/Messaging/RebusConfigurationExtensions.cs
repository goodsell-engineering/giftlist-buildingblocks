using System.Globalization;
using System.Text.Json;
using BuildingBlocks.Messaging.CorrelationId;
using BuildingBlocks.Messaging.RequestReply;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Rebus.Config;
using Rebus.Retry.FailFast;
using Rebus.Retry.Simple;

namespace BuildingBlocks.Messaging;

/// <summary>
/// The shared Rebus/RabbitMQ setup every host calls, so "which transport, which config key,
/// which pipeline steps" is decided once (ARCHITECTURE.md "Messaging") instead of four times.
///
/// <para>
/// GL-43: retry/error-queue behaviour is chosen here, explicitly, rather than left as whatever
/// Rebus 8's own defaults happen to be — so the policy is one decision every service inherits,
/// not four independent accidents:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Attempt count.</b> <see cref="MaxDeliveryAttempts"/> (5) unhandled exceptions are allowed
/// before a message is dead-lettered. Not configurable per host — CONVENTIONS.md "Messaging"
/// already requires every handler to be safe to run more than once, so redelivery is cheap by
/// design, and a uniform ceiling is what "applied to every consuming host" (GL-43) means: one
/// number every service can be reasoned about together, not five tuned in isolation.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Second-level retries: off.</b> Rebus's second-level retries hand an exhausted message to a
/// dedicated <c>IHandleMessages&lt;IFailed&lt;T&gt;&gt;</c> compensating handler before giving up
/// for good. No service in this system implements one, so leaving second-level retries at their
/// implicit default would silently double the effective attempt budget (2 x
/// <see cref="MaxDeliveryAttempts"/>) for no compensating behaviour to show for it. Disabled here,
/// explicitly, rather than left to be "whatever Rebus does".
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Transient vs. poison.</b> By default every exception is treated as transient: retried up to
/// <see cref="MaxDeliveryAttempts"/> times (Mongo hiccups, a momentarily-unreachable dependency,
/// a lost compare-and-set race under load — CONVENTIONS.md "Messaging" already assumes these are
/// ordinary and survivable) before landing in the error queue. The one exception classified as
/// poison up front, via <c>FailFastOn</c>, is a message whose body cannot be deserialized — it
/// will fail the exact same way on every redelivery, so retrying it five times only delays the
/// operator seeing it. The check is <c>FailFastOn&lt;FormatException&gt;(e =&gt;
/// e.InnerException is System.Text.Json.JsonException)</c>, not a bare
/// <see cref="JsonException"/> check: decompiling Rebus 8's own default serializer
/// (<c>Rebus.Serialization.Json.SystemTextJsonSerializer</c> — the one actually wired up; a
/// second, Newtonsoft.Json-based <c>Rebus.Serialization.Json.JsonSerializer</c> also exists in
/// Rebus.dll but is not the default, and this project got that wrong once already, see GL-43's
/// PR history) shows every exception <c>System.Text.Json.JsonSerializer.Deserialize</c> throws is
/// caught and re-thrown wrapped in a plain <see cref="FormatException"/>, so the raw
/// <see cref="JsonException"/> never reaches this pipeline step at all — matching on it directly
/// would silently never fire. Matching on <see cref="FormatException"/> alone would over-match
/// instead: that type is also ordinary .NET (<c>decimal.Parse</c>, <c>DateTime.Parse</c>, ...),
/// so a handler's own unrelated <see cref="FormatException"/> would be wrongly fast-failed too,
/// skipping retries a real transient cause of it might have survived. Checking that the inner
/// exception is specifically a <see cref="JsonException"/> is what narrows this back down to "the
/// message body itself did not parse". Nothing else is fast-failed: a handler bug (e.g. an unhandled
/// <see cref="InvalidOperationException"/>) is not distinguishable from a transient one by type
/// alone, and idempotent handlers make the extra attempts harmless, so the safer default is to let
/// the uniform retry ceiling above catch it rather than guess wrong and skip retries a blip would
/// have survived.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Error queue naming, per service.</b> <see cref="ErrorQueueName"/> derives
/// <c>{inputQueueName}.error</c> (e.g. <c>identity.error</c>, <c>giftlist.error</c>) from the
/// same <paramref name="inputQueueName"/> the host already passes — not Rebus's shared default of
/// a single queue literally named <c>error</c>. Every service in this system connects to the same
/// RabbitMQ broker, so a shared, unqualified <c>error</c> queue would interleave every service's
/// poisoned messages in one place with nothing beyond the message body to tell them apart. A
/// per-service name means an operator opening the RabbitMQ management UI sees which service a
/// poisoned message came from before opening it, and needs no per-host configuration to get
/// there — the name is computed, not typed out at each call site, so it cannot drift from the
/// input queue it is derived from.
/// </description>
/// </item>
/// </list>
/// <para>
/// What a poisoned message carries, once Rebus gives up on it: the original message body and
/// headers, unchanged, plus three headers Rebus adds — <c>rbs2-error-details</c> (the full
/// exception history across every attempt), <c>rbs2-source-queue</c> (this service's own input
/// queue, useful once the message is sitting in <c>{that}.error</c> instead) and
/// <c>rbs2-delivery-count</c> (how many attempts it took — 1 for a fast-failed poison message,
/// up to <see cref="MaxDeliveryAttempts"/> for an exhausted transient one). The correlation ID
/// header this class's own <see cref="CorrelationIdOutgoingStep"/> attaches on the way out
/// survives redelivery and dead-lettering too, since it is just another header Rebus carries
/// along — so a poisoned message in the error queue is still traceable back to the request that
/// produced it.
/// </para>
/// </summary>
public static class RebusConfigurationExtensions
{
    /// <summary>
    /// How many times Rebus attempts a message (the original delivery plus retries) before giving
    /// up and moving it to <see cref="ErrorQueueName"/>. See this class's own summary for why the
    /// number is fixed rather than configurable per host.
    /// </summary>
    public const int MaxDeliveryAttempts = 5;

    /// <summary>
    /// The queue a service's poisoned messages land in, derived from its own
    /// <paramref name="inputQueueName"/> so every service gets a distinct, predictable name with
    /// no per-host configuration (see this class's own summary). Exposed publicly so a host or a
    /// test can name the queue it expects a poisoned message to end up in without duplicating the
    /// <c>.error</c> suffix as a magic string.
    /// </summary>
    public static string ErrorQueueName(string inputQueueName) => $"{inputQueueName}.error";

    /// <summary>
    /// The configuration key the RabbitMQ connection string is read from. Fixed by the compose
    /// file as the <c>Rebus__ConnectionString</c> environment variable — do not rename.
    /// </summary>
    public const string ConnectionStringConfigKey = "Rebus:ConnectionString";

    /// <summary>
    /// Optional key overriding how long <see cref="IRequestReplyBridge"/> waits for a reply, in
    /// seconds (<c>Rebus__ReplyTimeoutSeconds</c> as an environment variable). Seconds rather
    /// than a <see cref="TimeSpan"/> string on purpose: <c>TimeSpan.Parse("5")</c> is five
    /// <i>days</i>, which is precisely the wrong failure mode for a timeout.
    /// </summary>
    public const string ReplyTimeoutSecondsConfigKey = "Rebus:ReplyTimeoutSeconds";

    /// <summary>The ~5s of ARCHITECTURE.md "Command → event flow", used when the key above is not set.</summary>
    private static readonly TimeSpan DefaultReplyTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Registers Rebus over RabbitMQ, with correlation-ID propagation and the request/reply
    /// bridge enabled, as the default bus. Additional configuration (sagas, timeouts,
    /// subscriptions, ...) can be layered on via <paramref name="configure"/>.
    /// </summary>
    /// <param name="inputQueueName">This service's own queue name (its command inbox).</param>
    /// <param name="configure">
    /// Optional further configuration, applied after the transport and correlation-ID step are
    /// set up. Receives the <see cref="IServiceProvider"/> so it can pull in e.g. an
    /// <see cref="MongoDB.Driver.IMongoDatabase"/> already registered by the host.
    /// </param>
    public static IServiceCollection AddBuildingBlocksRebus(
        this IServiceCollection services,
        IConfiguration configuration,
        string inputQueueName,
        Func<RebusConfigurer, IServiceProvider, RebusConfigurer>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputQueueName);

        var connectionString = configuration[ConnectionStringConfigKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing required configuration value '{ConnectionStringConfigKey}' " +
                "(set via the 'Rebus__ConnectionString' environment variable).");
        }

        services.AddSingleton<CorrelationIdAccessor>();
        services.AddSingleton<ICorrelationIdAccessor>(sp => sp.GetRequiredService<CorrelationIdAccessor>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(new RequestReplyOptions(ReadReplyTimeout(configuration)));
        services.AddSingleton<PendingRequestRegistry>();
        services.AddSingleton<IRequestReplyBridge, RequestReplyBridge>();

        services.AddRebus((rebus, serviceProvider) =>
        {
            var accessor = serviceProvider.GetRequiredService<ICorrelationIdAccessor>();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var pendingRequests = serviceProvider.GetRequiredService<PendingRequestRegistry>();

            var configurer = rebus
                .Transport(t => t.UseRabbitMq(connectionString, inputQueueName))
                .Options(o =>
                {
                    o.EnableCorrelationIdPropagation(accessor, loggerFactory);
                    o.EnableRequestReplyBridge(pendingRequests);
                    o.RetryStrategy(
                        errorQueueName: ErrorQueueName(inputQueueName),
                        maxDeliveryAttempts: MaxDeliveryAttempts,
                        secondLevelRetriesEnabled: false);
                    // A malformed body fails deserialization identically on every redelivery, so
                    // there is nothing to gain from spending the full retry budget on it — see
                    // this class's own summary ("Transient vs. poison") for why this checks
                    // FormatException.InnerException rather than JsonException directly, and why
                    // it doesn't match every FormatException.
                    o.FailFastOn<FormatException>(exception => exception.InnerException is JsonException);
                });

            return configure is null ? configurer : configure(configurer, serviceProvider);
        });

        return services;
    }

    private static TimeSpan ReadReplyTimeout(IConfiguration configuration)
    {
        var configured = configuration[ReplyTimeoutSecondsConfigKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultReplyTimeout;
        }

        // Misconfiguration fails at startup rather than degrading into a bridge that never times
        // out (or times out instantly) once a user is waiting on it.
        if (!double.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            !double.IsFinite(seconds) ||
            seconds <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration value '{ReplyTimeoutSecondsConfigKey}' must be a positive number " +
                $"of seconds, but was '{configured}'.");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
