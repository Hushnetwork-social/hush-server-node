using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IFeedsStorageService
{
    Task<bool> HasPersonalFeed(string publicSigningAddress);

    Task CreateFeed(Feed feed);
}