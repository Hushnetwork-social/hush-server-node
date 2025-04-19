using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IFeedMessageRepository : IRepository
{
    Task CreateFeedMessage(FeedMessage feedMessage);

    Task<IEnumerable<FeedMessage>> RetrieveLastFeedMessagesForAddress(string publicSigningAddress, BlockIndex blockIndex);
}
