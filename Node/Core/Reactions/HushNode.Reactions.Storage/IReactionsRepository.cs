using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;

namespace HushNode.Reactions.Storage;

public interface IReactionsRepository : IRepository
{
    Task<MessageReactionTally?> GetTallyAsync(FeedMessageId messageId);

    Task<MessageReactionTally?> GetTallyForUpdateAsync(FeedMessageId messageId);

    Task SaveTallyAsync(MessageReactionTally tally);

    Task<IEnumerable<MessageReactionTally>> GetTalliesForMessagesAsync(IEnumerable<FeedMessageId> messageIds);

    Task<ReactionNullifier?> GetNullifierAsync(byte[] nullifier);

    Task<bool> NullifierExistsAsync(byte[] nullifier);

    Task SaveNullifierAsync(ReactionNullifier nullifier);

    Task UpdateNullifierAsync(ReactionNullifier nullifier);

    Task SaveTransactionAsync(ReactionTransaction transaction);

    Task<IEnumerable<ReactionTransaction>> GetTransactionsFromBlockAsync(BlockIndex blockHeight);

    Task<ReactionNullifier?> GetNullifierWithBackupAsync(byte[] nullifier);

    /// <summary>
    /// Get reaction tallies for messages in specified feeds that have been updated
    /// since the given version. Used for incremental sync of reactions.
    /// </summary>
    /// <param name="feedIds">List of feed IDs the user belongs to</param>
    /// <param name="sinceVersion">Only return tallies with Version > sinceVersion</param>
    /// <returns>List of updated tallies, ordered by version, max 1000</returns>
    Task<IReadOnlyList<MessageReactionTally>> GetTalliesForFeedsAsync(
        IReadOnlyList<FeedId> feedIds,
        long sinceVersion);

    /// <summary>
    /// Get the next global tally version for sync purposes.
    /// This ensures all new/updated tallies have a version higher than any existing tally.
    /// </summary>
    /// <returns>Next version number to use (max existing version + 1, or 1 if no tallies exist)</returns>
    Task<long> GetNextGlobalTallyVersionAsync();
}
