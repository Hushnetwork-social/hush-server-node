using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IFeedsRepository : IRepository
{
    Task<bool> HasPersonalFeed(string publicSigningAddress);

    Task CreateFeed(Feed feed);
}
