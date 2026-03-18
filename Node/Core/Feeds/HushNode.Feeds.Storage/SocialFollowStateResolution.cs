using HushShared.Blockchain.BlockModel;
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

public sealed record SocialFollowBootstrapMutation(
    GroupFeed? InnerCircleToCreate,
    Feed? DirectChatToCreate,
    IReadOnlyList<GroupFeedParticipantEntity> InnerCircleParticipantsToAdd,
    IReadOnlyList<string> InnerCircleParticipantsToRejoin,
    BlockIndex? InnerCircleRejoinBlockIndex,
    GroupFeedKeyGenerationEntity? InnerCircleKeyGeneration,
    BlockIndex? InnerCircleLastUpdatedAtBlock);
