using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public interface IFeedMessageRepository : IRepository
{
    Task CreateFeedMessageAsync(FeedMessage feedMessage);

    Task<IEnumerable<FeedMessage>> RetrieveLastFeedMessagesForAddressAsync(string publicSigningAddress, BlockIndex blockIndex);

    Task<IEnumerable<FeedMessage>> RetrieveMessagesForFeedAsync(FeedId feedId, BlockIndex blockIndex);

    /// <summary>
    /// Retrieves a FeedMessage by its ID. Used by Protocol Omega to get AuthorCommitment.
    /// </summary>
    Task<FeedMessage?> GetFeedMessageByIdAsync(FeedMessageId messageId);

    /// <summary>
    /// Retrieves paginated messages for a feed with limit and optional fetch_latest mode.
    /// FEAT-052: Server Message Pagination.
    /// </summary>
    /// <param name="feedId">The feed ID to query.</param>
    /// <param name="sinceBlockIndex">Only return messages with BlockIndex >= this value (inclusive). Ignored if fetchLatest is true.</param>
    /// <param name="limit">Maximum number of messages to return.</param>
    /// <param name="fetchLatest">If true, return the latest N messages ordered ascending (ignore sinceBlockIndex).</param>
    /// <param name="beforeBlockIndex">Optional upper bound - only return messages with BlockIndex < this value (exclusive). Used for backward pagination (scroll-up).</param>
    /// <returns>Paginated result with messages, hasMore flag, and oldest block index.</returns>
    Task<PaginatedMessagesResult> GetPaginatedMessagesAsync(
        FeedId feedId,
        BlockIndex sinceBlockIndex,
        int limit,
        bool fetchLatest,
        BlockIndex? beforeBlockIndex = null);

    /// <summary>
    /// Checks if a message with the given ID exists in the database.
    /// FEAT-057: Server Message Idempotency - used for duplicate detection.
    /// </summary>
    /// <param name="messageId">The message ID to check.</param>
    /// <returns>True if a message with this ID exists, false otherwise.</returns>
    Task<bool> ExistsByMessageIdAsync(FeedMessageId messageId);
}
