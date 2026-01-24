using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public class FeedMessageRepository : RepositoryBase<FeedsDbContext>, IFeedMessageRepository
{
    public async Task CreateFeedMessageAsync(FeedMessage feedMessage) => 
        await this.Context.FeedMessages
            .AddAsync(feedMessage);

    public async Task<IEnumerable<FeedMessage>> RetrieveLastFeedMessagesForAddressAsync(
        string publicSigningAddress,
        BlockIndex blockIndex) =>
        await this.Context.FeedMessages
            .Where(x =>
                x.IssuerPublicAddress == publicSigningAddress &&
                x.BlockIndex >= blockIndex)
            .ToListAsync();

    public async Task<IEnumerable<FeedMessage>> RetrieveMessagesForFeedAsync(FeedId feedId, BlockIndex blockIndex) =>
        await this.Context.FeedMessages
            .Where(x =>
                x.FeedId == feedId &&
                x.BlockIndex >= blockIndex)
            .ToListAsync();

    public async Task<FeedMessage?> GetFeedMessageByIdAsync(FeedMessageId messageId) =>
        await this.Context.FeedMessages
            .FirstOrDefaultAsync(x => x.FeedMessageId == messageId);

    public async Task<PaginatedMessagesResult> GetPaginatedMessagesAsync(
        FeedId feedId,
        BlockIndex sinceBlockIndex,
        int limit,
        bool fetchLatest)
    {
        if (fetchLatest)
        {
            // Fetch latest N messages: order by BlockIndex descending, take limit, then reverse to ascending
            var latestMessages = await this.Context.FeedMessages
                .Where(x => x.FeedId == feedId)
                .OrderByDescending(x => x.BlockIndex)
                .Take(limit)
                .ToListAsync();

            // Reverse to get ascending order (oldest first)
            latestMessages.Reverse();

            if (latestMessages.Count == 0)
            {
                return new PaginatedMessagesResult(
                    Messages: latestMessages,
                    HasMoreMessages: false,
                    OldestBlockIndex: new BlockIndex(0));
            }

            var oldestBlockIndex = latestMessages[0].BlockIndex;

            // Check if there are older messages before the oldest returned
            var hasMore = await this.Context.FeedMessages
                .AnyAsync(x => x.FeedId == feedId && x.BlockIndex < oldestBlockIndex);

            return new PaginatedMessagesResult(
                Messages: latestMessages,
                HasMoreMessages: hasMore,
                OldestBlockIndex: oldestBlockIndex);
        }
        else
        {
            // Regular pagination: get messages >= sinceBlockIndex, ordered ascending
            // Fetch limit + 1 to detect if there are more messages
            var messages = await this.Context.FeedMessages
                .Where(x => x.FeedId == feedId && x.BlockIndex >= sinceBlockIndex)
                .OrderBy(x => x.BlockIndex)
                .Take(limit + 1)
                .ToListAsync();

            var hasMore = messages.Count > limit;

            // Only return up to limit messages
            if (hasMore)
            {
                messages = messages.Take(limit).ToList();
            }

            if (messages.Count == 0)
            {
                return new PaginatedMessagesResult(
                    Messages: messages,
                    HasMoreMessages: false,
                    OldestBlockIndex: new BlockIndex(0));
            }

            var oldestBlockIndex = messages[0].BlockIndex;

            // For forward pagination, hasMore indicates if there are MORE messages after this batch
            // But the spec says hasMore indicates if OLDER messages exist before the batch
            // So we need to check if there are messages before the oldest returned
            var hasOlderMessages = await this.Context.FeedMessages
                .AnyAsync(x => x.FeedId == feedId && x.BlockIndex < oldestBlockIndex);

            return new PaginatedMessagesResult(
                Messages: messages,
                HasMoreMessages: hasOlderMessages,
                OldestBlockIndex: oldestBlockIndex);
        }
    }
}