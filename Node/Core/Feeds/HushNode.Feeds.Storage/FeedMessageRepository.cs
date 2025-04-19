using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public class FeedMessageRepository : RepositoryBase<FeedsDbContext>, IFeedMessageRepository
{
    public async Task CreateFeedMessage(FeedMessage feedMessage) => 
        await this.Context.FeedMessages
            .AddAsync(feedMessage);

    public async Task<IEnumerable<FeedMessage>> RetrieveLastFeedMessagesForAddress(
        string publicSigningAddress, 
        BlockIndex blockIndex) => 
        await this.Context.FeedMessages
            .Where(x => 
                x.IssuerPublicAddress == publicSigningAddress && 
                x.BlockIndex > blockIndex)
            .ToListAsync();
}