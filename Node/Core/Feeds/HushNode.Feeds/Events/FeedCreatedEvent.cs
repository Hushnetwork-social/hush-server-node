using HushShared.Feeds.Model;

namespace HushNode.Feeds.Events;

/// <summary>
/// Published when a new feed is created.
/// Used by other modules (like Reactions) to perform actions after feed creation.
/// </summary>
public class FeedCreatedEvent
{
    public FeedId FeedId { get; }
    public string[] ParticipantPublicAddresses { get; }
    public FeedType FeedType { get; }

    public FeedCreatedEvent(FeedId feedId, string[] participantAddresses, FeedType feedType)
    {
        FeedId = feedId;
        ParticipantPublicAddresses = participantAddresses;
        FeedType = feedType;
    }
}
