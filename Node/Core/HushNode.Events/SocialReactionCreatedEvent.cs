using HushShared.Feeds.Model;

namespace HushNode.Events;

/// <summary>
/// Event published when a social reaction has been indexed and committed.
/// Used by FEAT-091 notification routing to resolve owner-targeted reaction notifications.
/// </summary>
public sealed class SocialReactionCreatedEvent(
    string actorPublicAddress,
    FeedId feedId,
    FeedMessageId targetMessageId)
{
    public string ActorPublicAddress { get; } = actorPublicAddress;

    public FeedId FeedId { get; } = feedId;

    public FeedMessageId TargetMessageId { get; } = targetMessageId;
}
