using Rebus.Config;
using Rebus.Logging;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;

namespace BuildingBlocks.Messaging.RequestReply;

internal static class RequestReplyPipelineConfigurationExtensions
{
    /// <summary>
    /// Inserts <see cref="PendingReplyIncomingStep"/> into Rebus's receive pipeline, which is
    /// the whole of the wiring the bridge needs on the bus — the send side needs no step at all.
    /// </summary>
    /// <remarks>
    /// Positioned before <see cref="ActivateHandlersStep"/> so the message body is already
    /// deserialized and no handler lookup has happened yet. Enabled in every service rather than
    /// only the ones that await replies: the step is inert for a process that never registers a
    /// pending request, and one wiring path with no half-configured variant is worth more than
    /// the microseconds a header lookup costs.
    /// </remarks>
    public static void EnableRequestReplyBridge(
        this OptionsConfigurer configurer,
        PendingRequestRegistry registry)
    {
        configurer.Decorate<IPipeline>(context =>
        {
            var pipeline = context.Get<IPipeline>();
            var log = context.Get<IRebusLoggerFactory>().GetLogger<PendingReplyIncomingStep>();

            return new PipelineStepInjector(pipeline)
                .OnReceive(
                    new PendingReplyIncomingStep(registry, log),
                    PipelineRelativePosition.Before,
                    typeof(ActivateHandlersStep));
        });
    }
}
