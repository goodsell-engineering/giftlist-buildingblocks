using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.MongoDb;
using Testcontainers.RabbitMq;

namespace BuildingBlocks.Testing;

/// <summary>
/// GL-93: replaces the default wait strategy Testcontainers.MongoDb and Testcontainers.RabbitMq
/// ship, on every builder in every suite, because both defaults read container history rather
/// than probing the live server, and history is exactly what a <c>.WithReuse(true)</c> container
/// accumulates across days of local runs.
///
/// <b>Mongo (the hang).</b> <c>MongoDbBuilder</c>'s built-in <c>WaitIndicateReadiness</c> counts
/// "Waiting for connections" lines in the container's logs and waits for the count to equal
/// exactly 1 (no credentials) or 2 (credentials — the default this codebase uses) — see
/// <c>MongoDbBuilder.cs</c>, private class <c>WaitIndicateReadiness</c>. It reads
/// <c>container.GetLogsAsync(since: container.StoppedTime)</c>, and <c>StoppedTime</c> is a
/// property on the *in-memory* <c>DockerContainer</c> instance, set only when that instance's own
/// <c>StopAsync</c> runs — never when a container is reused. A fixture that attaches to an
/// existing container via <c>.WithReuse(true)</c> therefore starts with <c>StoppedTime</c> at its
/// CLR default, <c>DateTime.MinValue</c>, so the log window is the container's *entire* history,
/// not this run's slice of it. A fresh container logs the marker exactly twice (the official
/// image starts a temporary unauthenticated <c>mongod</c> to create the root user, then the real
/// one); every restart after that logs it once more, so a reused container that has ever
/// restarted carries a count the equality check can never match again.
///
/// <b>This is not a rare-restart edge case — it is the second run of any suite, every time.</b>
/// Every fixture's <c>DisposeAsync</c> stops the container rather than deleting it (that is the
/// point of reuse), which leaves it <c>Exited</c>; the *next* run's <c>.WithReuse(true)</c>
/// attaches to that same container and restarts it. Run 1 ends with the marker count at 2 —
/// correct, matched, passed. Run 2 restarts that same container, driving the count to 3, which
/// the equality check can never match again on any future run. Confirmed end-to-end, not just by
/// log inspection: run 1 against a fresh <c>wizardly_cartwright</c> left it at count 2 and
/// passed; run 2 attached to it, drove it to 3, and hung. Also confirmed live against a real
/// six-day-old reuse container in that exact state: <c>docker logs</c> after a restart showed the
/// marker three times, not two. The wait strategy then retries once a second until its own
/// default one-hour timeout, which is the entire GL-93 hang — no test ever starts, because this
/// runs inside <c>InitializeAsync</c>, before the first test. "Hung forever on every integration
/// suite" (this ticket's own words) is exactly what this produces: not an occasional restart
/// landing badly, but the ordinary, unavoidable shape of a second local run.
///
/// This is a real upstream defect, not a misuse of the API: filed as
/// testcontainers-dotnet#1732, fixed for the replica-set pre-init path by #1735 (probe
/// <c>rs.status()</c> instead of counting), but left as-is on the standalone path this codebase's
/// fixtures use — byte-identical in 4.15.0, the latest tag at the time of writing, so upgrading
/// the package would not have fixed this. CONVENTIONS.md "Why there is no inbox" records that
/// this codebase runs Mongo standalone deliberately (no replica set, because change streams were
/// removed from the design) — it does not forbid a replica set, but it is why nothing here takes
/// the one path #1735 actually reaches, and we are squarely in the path it does not.
///
/// <b>RabbitMQ (the false-positive, not a hang).</b> The default wait strategy,
/// <c>UntilMessageIsLogged("Server startup complete")</c>, has the equivalent bug but a milder
/// symptom: it scopes its log window to <c>max(StoppedTime, CreatedTime)</c>, and for a reused
/// container <c>CreatedTime</c> is the *original* creation time from Docker, days old. A
/// <c>Regex.IsMatch</c> is a "contains" check, not an exact count, so it is satisfied the instant
/// it runs — the broker's log from days ago already contains the marker — and the fixture can
/// report ready before the freshly-restarted broker process is actually accepting connections.
/// This is a race, not a hang, which is why it was never the thing this ticket's repro caught,
/// but it is the same defect shape and gets a probe-based fix too.
///
/// <b>The fix, both cases: probe the live server, matching upstream's own #1735 direction.</b> A
/// ping or a diagnostics check answers on the record in front of it; it cannot be corrupted by
/// how many times the container has restarted or how long it has been reused. This is GL-93
/// acceptance criterion 4's "fixtures health-assert on attach rather than merely connecting": a
/// stale or alarmed container now fails within a bounded 30s instead of hanging for up to an
/// hour, and <see cref="StartReliablyAsync"/> below is what makes that failure loud as well as
/// fast.
///
/// <b>GL-93 review correction — the RabbitMQ probe cannot be a bare <c>docker exec</c>, and an
/// earlier version of this file got that wrong.</b> This is GL-56, re-introduced through a new
/// door: <c>rabbitmq:3.13-management</c> sets no <c>USER</c>, so a plain
/// <c>UntilCommandIsCompleted</c> exec runs as root — only the *server* process is dropped to uid
/// 999 (rabbitmq), via <c>gosu</c>, by the entrypoint. <c>rabbitmq-diagnostics</c> talks over
/// Erlang distribution, which requires <c>/var/lib/rabbitmq/.erlang.cookie</c> and *creates it if
/// missing*, owned by whoever ran the command. A probe run as root at t=0, before the server has
/// written its own cookie, wins that race and leaves the cookie <c>root:root 0400</c> — the
/// server, as uid 999, can then never read it: <c>eacces</c>, <c>BOOT FAILED</c>, and every test
/// in the suite fails within the first few seconds of <c>InitializeAsync</c>, on a container that
/// never gets the chance to become unready first. Confirmed directly, <c>docker run</c> only, no
/// dotnet involved: a root <c>rabbitmq-diagnostics check_running</c> from t=0 against a fresh
/// container/volume left the cookie <c>root:root</c> and the server dead 2/2 trials; the fix
/// below, and a no-probe control, both left the cookie <c>rabbitmq:rabbitmq</c> and the server
/// running 3/3. This exact mechanism is already documented in this repo, in
/// <c>giftlist-devenv/docker-compose.yml</c>'s rabbitmq healthcheck comment, and the fix already landed
/// there as GL-56 — <c>gosu rabbitmq rabbitmq-diagnostics -q ping</c>. Run the probe as the
/// rabbitmq user here too, so any cookie it creates is one the server can also read; do not
/// "simplify" this back to a bare command.
///
/// <b>GL-93 review correction (S2) — <c>check_running</c> alone does not detect an alarmed
/// broker, and both this file and the README promised it did.</b> <c>check_running</c> only asks
/// whether the RabbitMQ application is running; it is orthogonal to resource alarms. Verified
/// directly: against a broker with <c>rabbitmqctl set_vm_memory_high_watermark absolute 1</c>
/// applied, <c>gosu rabbitmq rabbitmq-diagnostics check_running</c> still exits 0, while
/// <c>gosu rabbitmq rabbitmq-diagnostics check_local_alarms</c> exits 69 and names the alarm
/// ("Memory alarm on node ..."). A memory-alarmed broker blocks publishers, so a probe that only
/// checked <c>check_running</c> would attach "ready" and relocate GL-93's hang into the Rebus
/// host's first publish instead of fixing it. <c>check_local_alarms</c>, not the cluster-wide
/// <c>check_alarms</c>, because this is always a single, unclustered test node — verified both
/// give the same exit codes (0 healthy, 69 alarmed) on one, but the "local" check is the one that
/// says what it means here. Also verified the healthy case does not regress: the compound check
/// (<c>check_running &amp;&amp; check_local_alarms</c>) completes in ~1.3s against a healthy
/// broker — a false failure would be exactly as bad as a missed alarm, and this is not one.
///
/// <b>Mongo does not share the RabbitMQ root-exec defect — checked directly, not assumed,
/// because <c>ExecScriptAsync</c> also execs with no user specified.</b> <c>mongo:7</c> likewise
/// sets no <c>USER</c>, so <see cref="MongoRespondsToPing"/>'s exec is root too, but
/// <c>mongosh</c> authenticates over the MongoDB wire protocol (SCRAM, using the same
/// username/password the server already has) rather than through a shared local secret file the
/// server must also read — there is no Mongo equivalent of the Erlang cookie for a client to race
/// the server into creating with the wrong owner. Verified against a fresh <c>mongo:7</c>
/// container hammered with this exact probe once a second from t=1s (spanning connection-refused
/// and pre-auth windows): the server booted and stayed up every time, and every file under
/// <c>/data/db</c> the server itself needs stayed <c>mongodb:mongodb</c>. <c>mongosh</c> does
/// write a root-owned artifact, its own client-side telemetry cache at
/// <c>/data/db/.mongodb/mongosh</c> — never read by <c>mongod</c>, so it is not the failure mode
/// this class of bug produces, but it is not bounded either: 25 polls over one run left 22
/// per-invocation <c>_log</c> files plus a config file in there, one per poll, and this fixture's
/// whole reason to reuse a container is to keep it around for many runs. Harmless to correctness,
/// real as accumulating clutter inside the data directory of a container that lives for exactly
/// that long — do not read "cosmetic" as "bounded".
/// </summary>
public static class ReliableReadiness
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static MongoDbBuilder WithReliableWaitStrategy(this MongoDbBuilder builder) =>
        builder.WithWaitStrategy(Wait.ForUnixContainer()
            .AddCustomWaitStrategy(new MongoRespondsToPing(), w => w.WithTimeout(Timeout)));

    // GL-93/GL-56/S2: must run as the rabbitmq user (never a bare exec — GL-56), and must check
    // for alarms as well as the application being up (never check_running alone — S2), or this
    // probe reports a broker ready that a publish will then hang against. See the class doc
    // comment's "GL-93 review correction" sections for both mechanisms and how they were verified.
    public static RabbitMqBuilder WithReliableWaitStrategy(this RabbitMqBuilder builder) =>
        builder.WithWaitStrategy(Wait.ForUnixContainer()
            .UntilCommandIsCompleted(
                "gosu rabbitmq rabbitmq-diagnostics check_running "
                + "&& gosu rabbitmq rabbitmq-diagnostics check_local_alarms",
                w => w.WithTimeout(Timeout)));

    /// <summary>
    /// GL-93 review (S3): the wait-strategy machinery throws a bare, parameterless
    /// <see cref="TimeoutException"/> when a probe never succeeds — <c>WaitStrategy.WaitUntilAsync</c>
    /// runs it inside a captured local function, so neither the container, its image, nor which
    /// check failed ever reaches the caller. A sick container previously gave 30 seconds of
    /// silence then a contentless exception: fast, but not loud, and GL-93 acceptance criterion 4
    /// asks for both. One helper here, used by every fixture's <c>InitializeAsync</c> in place of
    /// a bare <c>StartAsync</c>, rather than a try/catch repeated at each call site.
    /// </summary>
    public static async Task StartReliablyAsync(
        this IContainer container, string suiteLabel, CancellationToken ct = default)
    {
        try
        {
            await container.StartAsync(ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            var probe = container switch
            {
                MongoDbContainer => "db.adminCommand({ping:1}) never answered",
                RabbitMqContainer => "check_running && check_local_alarms never both succeeded",
                _ => "its readiness probe never succeeded",
            };

            throw new TimeoutException(
                $"GL-93: suite \"{suiteLabel}\"'s {container.Image.FullName} container "
                + $"(\"{container.Name.TrimStart('/')}\", id {container.Id}) — {probe} within {Timeout}. "
                + $"Try `docker logs {container.Name.TrimStart('/')}` and "
                + $"`docker exec {container.Name.TrimStart('/')} ...` "
                + "directly — this is very likely a genuinely broken or alarmed container, not a bug in the "
                + "check itself. See BuildingBlocks.Testing.ReliableReadiness for exactly what runs.",
                ex);
        }
    }

    /// <summary>
    /// Mirrors the exit-code convention <c>MongoDbBuilder</c>'s own replica-set probes use
    /// (<c>WaitReplicaSetEnabled</c>/<c>WaitReplicaSetPrimary</c>) rather than inventing a new one:
    /// <c>quit(0)</c> on success, non-zero otherwise, so a thrown exception (not yet accepting
    /// connections at all) and a false ping (accepting connections but not answering "ok": 1) both
    /// count as "not ready yet" the same way.
    /// </summary>
    private sealed class MongoRespondsToPing : IWaitUntil
    {
        private const string ScriptContent = "try{quit(db.adminCommand({ping:1}).ok===1?0:1);}catch(e){quit(1);}";

        public async Task<bool> UntilAsync(IContainer container)
        {
            var execResult = await ((MongoDbContainer)container).ExecScriptAsync(ScriptContent)
                .ConfigureAwait(false);

            return 0L.Equals(execResult.ExitCode);
        }
    }
}
