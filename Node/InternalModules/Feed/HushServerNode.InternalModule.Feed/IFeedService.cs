using HushEcosystem.Model.Blockchain;

namespace HushServerNode.InternalModule.Feed;

public interface IFeedService
{
    Task AddMessage(FeedMessage feedMessage, long blockIndex);

    Task AddFeedAsync(HushEcosystem.Model.Blockchain.Feed feed, long blockIndex);
}
