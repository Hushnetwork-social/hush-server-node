using HushShared.Feeds.Model;

namespace HushNode.Events;

/// <summary>
/// Event published when a new feed message is created and saved to the database.
/// Used by the notification system to trigger real-time notifications.
/// </summary>
public class NewFeedMessageCreatedEvent(FeedMessage feedMessage)
{
    /// <summary>
    /// The feed message that was created.
    /// </summary>
    public FeedMessage FeedMessage { get; } = feedMessage;
}
