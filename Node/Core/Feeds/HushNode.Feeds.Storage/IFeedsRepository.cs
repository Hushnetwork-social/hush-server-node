using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;
using HushShared.Blockchain.BlockModel;

namespace HushNode.Feeds.Storage;

public interface IFeedsRepository : IRepository
{
    Task<bool> HasPersonalFeed(string publicSigningAddress);

    Task CreateFeed(Feed feed);

    Task<IEnumerable<Feed>> RetrieveFeedsForAddress(string publicSigningAddress, BlockIndex blockIndex);
}
