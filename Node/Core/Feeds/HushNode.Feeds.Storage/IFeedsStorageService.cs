using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IFeedsStorageService
{
    Task<bool> HasPersonalFeed(string publicSigningAddress);

    Task<bool> IsFeedIsBlockchain(FeedId feedId);

    Task CreateFeed(Feed feed);

    /// <summary>
    /// Atomically creates a personal feed if one doesn't exist for the user.
    /// Returns true if created, false if personal feed already exists.
    /// </summary>
    Task<bool> CreatePersonalFeedIfNotExists(Feed feed, string publicSigningAddress);

    Task<IEnumerable<Feed>> RetrieveFeedsForAddress(string publicSigningAddress, BlockIndex blockIndex);
}