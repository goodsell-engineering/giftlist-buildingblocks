using Rebus.Config;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.Pipeline.Send;

namespace BuildingBlocks.Messaging.CorrelationId;

internal static class CorrelationIdPipelineConfigurationExtensions
{
    /// <summary>
    /// Inserts <see cref="CorrelationIdIncomingStep"/> and <see cref="CorrelationIdOutgoingStep"/>
    /// into Rebus's pipeline — the one place this is wired, per ARCHITECTURE.md "Cross-cutting concerns", rather than
    /// every handler propagating a correlation ID by hand.
    /// </summary>
    public static void EnableCorrelationIdPropagation(
        this OptionsConfigurer configurer,
        ICorrelationIdAccessor accessor)
    {
        configurer.Decorate<IPipeline>(context =>
        {
            var pipeline = context.Get<IPipeline>();

            return new PipelineStepInjector(pipeline)
                .OnReceive(
                    new CorrelationIdIncomingStep(accessor),
                    PipelineRelativePosition.Before,
                    typeof(DeserializeIncomingMessageStep))
                .OnSend(
                    new CorrelationIdOutgoingStep(accessor),
                    PipelineRelativePosition.Before,
                    typeof(AssignDefaultHeadersStep));
        });
    }
}
