using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;

namespace BuildingBlocks.Logging;

/// <summary>
/// GL-45: turns on scope rendering for the default console logger — <c>Microsoft.Extensions
/// .Logging</c>'s simple console formatter defaults <c>IncludeScopes</c> to <see langword="false"/>,
/// so without this the correlation id
/// <see cref="BuildingBlocks.Messaging.CorrelationId.CorrelationIdLogging.BeginScope"/> pushes at
/// the two ingress points (a Rebus message's own <c>CorrelationIdIncomingStep</c>, and whatever
/// HTTP-facing ingress a Host adds in front of it) never reaches an operator's console — it would
/// sit correctly in <see cref="System.Threading.ExecutionContext"/>, provably (a real structured
/// sink would see it), but unread by the one sink every service actually ships with today.
/// Configures the options object the already-registered console provider reads, rather than
/// adding a second console provider (<c>AddSimpleConsole</c> would), so calling this from every
/// Host's <c>Program.cs</c> — the same place <c>AddBuildingBlocksMongo</c>/<c>AddBuildingBlocksRebus</c>
/// /<c>AddBuildingBlocksHealthChecks</c> are already called from — is "one more line", not a second
/// logging pipeline. See this repo's PR for GL-45 for why Microsoft.Extensions.Logging was kept
/// rather than introducing Serilog: no service in this system references Serilog today (a `grep`
/// across all seven repos turns up nothing but two doc-comments), and ARCHITECTURE.md "Cross-cutting
/// concerns" names it alongside OpenTelemetry as the target stack without either being wired up
/// yet — adding a new logging framework to four hosts to satisfy a "structured logs" requirement
/// that <see cref="ILogger"/> scopes already satisfy would be scope growth the issue text
/// ("correlation ID through the Rebus pipeline step; structured logs") does not ask for.
/// </summary>
public static class LoggingServiceCollectionExtensions
{
    public static IServiceCollection AddBuildingBlocksLogging(this IServiceCollection services)
    {
        services.Configure<SimpleConsoleFormatterOptions>(options => options.IncludeScopes = true);
        return services;
    }
}
