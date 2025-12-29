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

    public async Task CreateGroupFeed(GroupFeed groupFeed) =>
        await this.Context.GroupFeeds
            .AddAsync(groupFeed);

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

    public async Task<IReadOnlyList<FeedId>> GetFeedIdsForUserAsync(string publicAddress) =>
        await this.Context.FeedParticipants
            .Where(fp => fp.ParticipantPublicAddress == publicAddress)
            .Select(fp => fp.FeedId)
            .Distinct()
            .ToListAsync();

    public async Task UpdateFeedsBlockIndexForParticipantAsync(string publicSigningAddress, BlockIndex blockIndex)
    {
        // Get all feed IDs where this user is a participant
        var feedIds = await this.Context.FeedParticipants
            .Where(fp => fp.ParticipantPublicAddress == publicSigningAddress)
            .Select(fp => fp.FeedId)
            .Distinct()
            .ToListAsync();

        if (feedIds.Count == 0) return;

        // Update BlockIndex for all these feeds
        await this.Context.Feeds
            .Where(f => feedIds.Contains(f.FeedId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.BlockIndex, blockIndex));
    }
}
