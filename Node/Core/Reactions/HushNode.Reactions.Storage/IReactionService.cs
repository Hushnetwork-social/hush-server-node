using HushShared.Feeds.Model;
using HushShared.Reactions.Model;

namespace HushNode.Reactions.Storage;

/// <summary>
/// Service for querying anonymous reactions.
///
/// NOTE: Reaction submission is handled via blockchain transactions.
/// See ReactionTransactionHandler for submission processing.
/// </summary>
public interface IReactionService
{
    /// <summary>
    /// Gets aggregated tallies for a set of messages.
    /// </summary>
    Task<IEnumerable<MessageReactionTally>> GetTalliesAsync(FeedId feedId, IEnumerable<FeedMessageId> messageIds);

    /// <summary>
    /// Checks if a nullifier exists (for client-side duplicate detection).
    /// </summary>
    Task<bool> NullifierExistsAsync(byte[] nullifier);

    /// <summary>
    /// Gets the encrypted emoji backup for a nullifier (for cross-device recovery).
    /// </summary>
    Task<byte[]?> GetReactionBackupAsync(byte[] nullifier);
}
