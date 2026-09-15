using BuildingBlocks.Messaging.RequestReply;
using BuildingBlocks.Results;
using Rebus.Bus;
using Rebus.Handlers;

namespace BuildingBlocks.IntegrationTests.Support;

/// <summary>A minimal command, standing in for a real service's request/reply command (e.g. Identity's SignUp).</summary>
public sealed record ProbeRequest(string Scenario);

public sealed record ProbeReply(string Value);

/// <summary>
/// The responder half of a request/reply exchange, thin exactly the way a real handler is
/// (CONVENTIONS.md "Messaging"): reply on success, <see cref="ReplyFault"/> on failure, nothing on
/// <c>"never-replies"</c> so the requester's timeout path has something real to hit.
/// </summary>
public sealed class ProbeRequestHandler(IBus bus) : IHandleMessages<ProbeRequest>
{
    public const string Success = "success";
    public const string Fault = "fault";
    public const string NeverReplies = "never-replies";

    public static readonly Error FaultError = new("test.probe_failed", "The probe deliberately failed.", ErrorKind.Conflict);

    public async Task Handle(ProbeRequest message)
    {
        switch (message.Scenario)
        {
            case Success:
                await bus.Reply(new ProbeReply("ok"));
                break;
            case Fault:
                await bus.Reply(ReplyFault.From(FaultError));
                break;
            case NeverReplies:
                // Deliberately does nothing — stands in for a handler that is still working, or
                // whose reply is lost, so the requester's timeout is exercised for real.
                break;
            default:
                throw new InvalidOperationException($"Unknown probe scenario '{message.Scenario}'.");
        }
    }
}
