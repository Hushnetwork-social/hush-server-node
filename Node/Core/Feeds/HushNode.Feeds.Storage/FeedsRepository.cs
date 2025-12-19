using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;
using HushShared.Blockchain.BlockModel;

namespace HushNode.Feeds.Storage;

public class FeedsRepository : RepositoryBase<FeedsDbContext>, IFeedsRepository
{
    public async Task<bool> HasPersonalFeed(string publicSigningAddress) => 
        await this.Context.FeedParticipants
            .AnyAsync(x => 
                x.ParticipantPublicAddress == publicSigningAddress &&
                x.ParticipantType == ParticipantType.Owner &&
                x.Feed.FeedType == FeedType.Personal);

    public async Task<bool> IsFeedInBlockchain(FeedId feedId) => 
        await this.Context.Feeds
            .AnyAsync(x => x.FeedId == feedId);

    public async Task CreateFeed(Feed feed) => 
        await this.Context.Feeds
            .AddAsync(feed);

    public async Task<IEnumerable<Feed>> RetrieveFeedsForAddress(
        string publicSigningAddress,
        BlockIndex blockIndex) =>
        await this.Context.Feeds
            .Include(x => x.Participants)
            .Where(x =>
                x.Participants.Any(participant => participant.ParticipantPublicAddress == publicSigningAddress) &&
                x.BlockIndex > blockIndex)
            .ToListAsync();

    public async Task<Feed?> GetFeedByIdAsync(FeedId feedId) =>
        await this.Context.Feeds
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.FeedId == feedId);
}
