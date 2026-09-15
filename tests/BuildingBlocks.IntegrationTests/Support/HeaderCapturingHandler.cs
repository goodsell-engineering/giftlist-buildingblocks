using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;

namespace BuildingBlocks.IntegrationTests.Support;

/// <summary>
/// A responder that records the <c>rbs2-msg-id</c> header it actually received on the wire,
/// rather than replying — proof of the outgoing leg of the message-id assumption
/// (<see cref="Messaging.RequestReply.MessageIdHeaderPropagationTests"/>): what the requester set
/// explicitly is what a real broker delivered, not what
/// <c>Rebus.Pipeline.Send.AssignDefaultHeadersStep</c> would have generated had it not seen one
/// already set.
/// </summary>
public sealed class HeaderCapturingHandler(HeaderCapture capture) : IHandleMessages<ProbeRequest>
{
    public Task Handle(ProbeRequest message)
    {
        MessageContext.Current.Headers.TryGetValue(Headers.MessageId, out var messageId);
        capture.Completion.TrySetResult(messageId);
        return Task.CompletedTask;
    }
}

/// <summary>Registered as a singleton in the responder's own DI container so the test can await it.</summary>
public sealed class HeaderCapture
{
    public TaskCompletionSource<string?> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
