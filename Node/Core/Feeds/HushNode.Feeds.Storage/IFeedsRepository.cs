using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.Storage;

public interface IFeedsRepository : IRepository
{
    Task<bool> HasPersonalFeed(string publicSigningAddress);
}
