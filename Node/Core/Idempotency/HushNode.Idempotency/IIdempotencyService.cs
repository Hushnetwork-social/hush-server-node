using HushNetwork.proto;
using HushShared.Feeds.Model;

namespace HushNode.Idempotency;

/// <summary>
/// Service for checking message idempotency to prevent duplicate transaction processing.
/// FEAT-057: Server Message Idempotency.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Checks if a message with the given ID already exists in MemPool or Database.
    /// </summary>
    /// <param name="messageId">The message ID to check.</param>
    /// <returns>
    /// TransactionStatus indicating the result:
    /// - ACCEPTED: Message is new and can be processed
    /// - ALREADY_EXISTS: Message already exists in the database (confirmed)
    /// - PENDING: Message is currently in the MemPool (awaiting confirmation)
    /// </returns>
    Task<TransactionStatus> CheckAsync(FeedMessageId messageId);

    /// <summary>
    /// Attempts to track a message ID in the MemPool after successful validation.
    /// Uses thread-safe operations to prevent race conditions.
    /// </summary>
    /// <param name="messageId">The message ID to track.</param>
    /// <returns>True if successfully added, false if already tracked.</returns>
    bool TryTrackInMemPool(FeedMessageId messageId);

    /// <summary>
    /// Removes message IDs from MemPool tracking when transactions are included in a block.
    /// Called after block finalization.
    /// </summary>
    /// <param name="messageIds">The message IDs to remove from tracking.</param>
    void RemoveFromTracking(IEnumerable<FeedMessageId> messageIds);
}
