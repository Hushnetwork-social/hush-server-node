using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IFeedMessageStorageService
{
    Task CreateFeedMessage(FeedMessage feedMessage);

    Task<IEnumerable<FeedMessage>> RetrieveLastFeedMessagesForAddress(string publicSigningAddress, BlockIndex blockIndex);
}
