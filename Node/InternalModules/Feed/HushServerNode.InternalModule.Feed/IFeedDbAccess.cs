using HushServerNode.InternalModule.Feed.Cache;

namespace HushServerNode.InternalModule.Feed;

public interface IFeedDbAccess
{
    bool UserHasPersonalFeed(string address);

    bool FeedExists(string feedId);

    FeedEntity? GetFeed(string feedId);

    Task CreatePersonalFeed(
        string feedTitle,
        string feedId,
        int feedType,
        string personalFeedOwnerAddress,
        string publicEncryptAddress,
        string privateEncryptKey,
        long blockIndex);

    Task CreateChatFeed(
        string feedId,
        int feedType,
        string chatParticipantAddress,
        string chatParticipantPublicEncryptAddress,
        string chatParticipantPrivateEncryptKey,
        long blockIndex);

    Task AddParticipantToChatFeed(
        string feedId,
        string chatParticipantAddress,
        string chatParticipantPublicEncryptAddress,
        string chatParticipantPrivateEncryptKey,
        long blockIndex);

    IEnumerable<FeedEntity> GetUserFeeds(string address);

    IEnumerable<FeedMessageEntity> GetFeedMessages(string feedId, double blockIndex);

    Task SaveMessageAsync(FeedMessageEntity feedMessage);

    bool UserHasFeeds(string address);
}