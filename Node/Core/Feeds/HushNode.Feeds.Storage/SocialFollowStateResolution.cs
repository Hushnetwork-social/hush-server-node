using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public sealed record SocialFollowStateResolution(
    bool IsFollowing,
    bool CanFollow);

public sealed record SocialFollowBootstrapState(
    bool AlreadyFollowing,
    bool HasInnerCircle,
    bool HasDirectChat,
    FeedId? InnerCircleFeedId);
