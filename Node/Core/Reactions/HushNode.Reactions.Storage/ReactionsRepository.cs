using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;

namespace HushNode.Reactions.Storage;

public class ReactionsRepository : RepositoryBase<ReactionsDbContext>, IReactionsRepository
{
    public async Task<MessageReactionTally?> GetTallyAsync(FeedMessageId messageId) =>
        await this.Context.MessageReactionTallies
            .FirstOrDefaultAsync(x => x.MessageId == messageId);

    public async Task<MessageReactionTally?> GetTallyForUpdateAsync(FeedMessageId messageId)
    {
        // Use raw SQL for row-level locking (FOR UPDATE)
        // This prevents concurrent updates to the same tally
        var tally = await this.Context.MessageReactionTallies
            .FromSqlRaw(
                @"SELECT * FROM ""Reactions"".""MessageReactionTally""
                  WHERE ""MessageId"" = {0} FOR UPDATE",
                messageId.ToString())
            .FirstOrDefaultAsync();

        return tally;
    }

    public async Task SaveTallyAsync(MessageReactionTally tally)
    {
        var existing = await this.Context.MessageReactionTallies
            .FirstOrDefaultAsync(x => x.MessageId == tally.MessageId);

        if (existing != null)
        {
            this.Context.Entry(existing).CurrentValues.SetValues(tally);
        }
        else
        {
            await this.Context.MessageReactionTallies.AddAsync(tally);
        }
    }

    public async Task<IEnumerable<MessageReactionTally>> GetTalliesForMessagesAsync(
        IEnumerable<FeedMessageId> messageIds)
    {
        var messageIdList = messageIds.ToList();
        return await this.Context.MessageReactionTallies
            .Where(x => messageIdList.Contains(x.MessageId))
            .ToListAsync();
    }

    public async Task<ReactionNullifier?> GetNullifierAsync(byte[] nullifier) =>
        await this.Context.ReactionNullifiers
            .FirstOrDefaultAsync(x => x.Nullifier == nullifier);

    public async Task<bool> NullifierExistsAsync(byte[] nullifier) =>
        await this.Context.ReactionNullifiers
            .AnyAsync(x => x.Nullifier == nullifier);

    public async Task SaveNullifierAsync(ReactionNullifier nullifier) =>
        await this.Context.ReactionNullifiers.AddAsync(nullifier);

    public async Task UpdateNullifierAsync(ReactionNullifier nullifier)
    {
        var existing = await this.Context.ReactionNullifiers
            .FirstOrDefaultAsync(x => x.Nullifier == nullifier.Nullifier);

        if (existing != null)
        {
            this.Context.Entry(existing).CurrentValues.SetValues(nullifier);
        }
    }

    public async Task SaveTransactionAsync(ReactionTransaction transaction) =>
        await this.Context.ReactionTransactions.AddAsync(transaction);

    public async Task<IEnumerable<ReactionTransaction>> GetTransactionsFromBlockAsync(BlockIndex blockHeight) =>
        await this.Context.ReactionTransactions
            .Where(x => x.BlockHeight >= blockHeight)
            .OrderBy(x => x.BlockHeight)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync();

    public async Task<ReactionNullifier?> GetNullifierWithBackupAsync(byte[] nullifier) =>
        await this.Context.ReactionNullifiers
            .Where(x => x.Nullifier == nullifier && x.EncryptedEmojiBackup != null)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<MessageReactionTally>> GetTalliesForFeedsAsync(
        IReadOnlyList<FeedId> feedIds,
        long sinceVersion)
    {
        if (feedIds.Count == 0)
        {
            return Array.Empty<MessageReactionTally>();
        }

        return await this.Context.MessageReactionTallies
            .Where(t => feedIds.Contains(t.FeedId))
            .Where(t => t.Version > sinceVersion)
            .Where(t => t.TotalCount > 0)  // Skip messages with no reactions
            .OrderBy(t => t.Version)
            .Take(1000)  // Limit to prevent huge responses
            .ToListAsync();
    }

    public async Task<long> GetNextGlobalTallyVersionAsync()
    {
        var maxVersion = await this.Context.MessageReactionTallies
            .MaxAsync(t => (long?)t.Version) ?? 0;

        return maxVersion + 1;
    }
}
