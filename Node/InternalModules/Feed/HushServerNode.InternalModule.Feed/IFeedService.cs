using HushEcosystem.Model.Blockchain;
using HushServerNode.InternalModule.Feed.Cache;

namespace HushServerNode.InternalModule.Feed;

public interface IFeedService
{
    Task AddMessage(FeedMessage feedMessage, long blockIndex);

    Task AddFeedAsync(HushEcosystem.Model.Blockchain.Feed feed, long blockIndex);

    bool UserHasFeeds(string address);

    IEnumerable<FeedEntity> GetUserFeeds(string address);

    FeedEntity? GetFeed(string feedId);

    IEnumerable<FeedMessageEntity> GetFeedMessages(string feedId, long blockIndex);
}
