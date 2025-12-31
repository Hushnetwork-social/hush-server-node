using HushShared.Feeds.Model;

namespace HushNode.Feeds.Events;

/// <summary>
/// Published when a key rotation is completed for a group feed.
/// Enables clients to be notified when a new KeyGeneration is available
/// so they can fetch their encrypted key.
/// </summary>
public class KeyRotationCompletedEvent
{
    public FeedId FeedId { get; }
    public int NewKeyGeneration { get; }
    public string[] MemberPublicAddresses { get; }
    public RotationTrigger Trigger { get; }

    public KeyRotationCompletedEvent(
        FeedId feedId,
        int newKeyGeneration,
        string[] memberPublicAddresses,
        RotationTrigger trigger)
    {
        FeedId = feedId;
        NewKeyGeneration = newKeyGeneration;
        MemberPublicAddresses = memberPublicAddresses;
        Trigger = trigger;
    }
}
