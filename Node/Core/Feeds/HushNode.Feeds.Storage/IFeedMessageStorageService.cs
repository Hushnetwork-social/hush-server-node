using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IFeedMessageStorageService
{
    Task CreateFeedMessageAsync(FeedMessage feedMessage);

    Task<IEnumerable<FeedMessage>> RetrieveLastFeedMessagesForAddressAsync(string publicSigningAddress, BlockIndex blockIndex);

    Task<IEnumerable<FeedMessage>> RetrieveLastFeedMessagesForFeedAsync(FeedId feedId, BlockIndex blockIndex);

    /// <summary>
    /// Retrieves a FeedMessage by its ID. Used by Protocol Omega to get AuthorCommitment.
    /// </summary>
    Task<FeedMessage?> GetFeedMessageByIdAsync(FeedMessageId messageId);
}
