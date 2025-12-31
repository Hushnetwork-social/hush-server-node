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
                x.BlockIndex > blockIndex)
            .ToListAsync();

    public async Task<IEnumerable<FeedMessage>> RetrieveMessagesForFeedAsync(FeedId feedId, BlockIndex blockIndex) =>
        await this.Context.FeedMessages
            .Where(x =>
                x.FeedId == feedId &&
                x.BlockIndex > blockIndex)
            .ToListAsync();

    public async Task<FeedMessage?> GetFeedMessageByIdAsync(FeedMessageId messageId) =>
        await this.Context.FeedMessages
            .FirstOrDefaultAsync(x => x.FeedMessageId == messageId);
}