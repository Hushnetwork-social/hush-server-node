using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IFeedMessageRepository : IRepository
{
    Task CreateFeedMessageAsync(FeedMessage feedMessage);

    Task<IEnumerable<FeedMessage>> RetrieveLastFeedMessagesForAddressAsync(string publicSigningAddress, BlockIndex blockIndex);

    Task<IEnumerable<FeedMessage>> RetrieveMessagesForFeedAsync(FeedId feedId, BlockIndex blockIndex);
}
