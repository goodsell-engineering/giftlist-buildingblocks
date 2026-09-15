namespace BuildingBlocks.Messaging.RequestReply;

/// <summary>
/// How long <see cref="IRequestReplyBridge"/> waits before giving up, when a call does not name
/// its own timeout. Set from configuration by
/// <see cref="RebusConfigurationExtensions.AddBuildingBlocksRebus"/>.
/// </summary>
/// <param name="ReplyTimeout">
/// The default wait. ARCHITECTURE.md "Command → event flow" puts this at ~5s: long enough to cover a cold handler
/// plus a Mongo round trip, short enough that a user staring at a spinner gets the "still working
/// on it" fallback instead of an apparently hung page.
/// </param>
internal sealed record RequestReplyOptions(TimeSpan ReplyTimeout);
