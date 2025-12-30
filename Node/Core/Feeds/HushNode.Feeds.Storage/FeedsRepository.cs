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

    // ===== Group Feed Admin Operations (FEAT-009) =====

    public async Task<GroupFeed?> GetGroupFeedAsync(FeedId feedId) =>
        await this.Context.GroupFeeds
            .Include(g => g.Participants)
            .FirstOrDefaultAsync(g => g.FeedId == feedId);

    public async Task<GroupFeedParticipantEntity?> GetGroupFeedParticipantAsync(FeedId feedId, string publicAddress) =>
        await this.Context.GroupFeedParticipants
            .FirstOrDefaultAsync(p =>
                p.FeedId == feedId &&
                p.ParticipantPublicAddress == publicAddress &&
                p.LeftAtBlock == null); // Only active participants

    public async Task UpdateParticipantTypeAsync(FeedId feedId, string publicAddress, ParticipantType newType)
    {
        await this.Context.GroupFeedParticipants
            .Where(p => p.FeedId == feedId && p.ParticipantPublicAddress == publicAddress)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.ParticipantType, newType));
    }

    public async Task<bool> IsAdminAsync(FeedId feedId, string publicAddress) =>
        await this.Context.GroupFeedParticipants
            .AnyAsync(p =>
                p.FeedId == feedId &&
                p.ParticipantPublicAddress == publicAddress &&
                p.ParticipantType == ParticipantType.Admin &&
                p.LeftAtBlock == null);

    public async Task<int> GetAdminCountAsync(FeedId feedId) =>
        await this.Context.GroupFeedParticipants
            .CountAsync(p =>
                p.FeedId == feedId &&
                p.ParticipantType == ParticipantType.Admin &&
                p.LeftAtBlock == null);

    // ===== Group Feed Metadata Operations (FEAT-009 Phase 4) =====

    public async Task UpdateGroupFeedTitleAsync(FeedId feedId, string newTitle)
    {
        await this.Context.GroupFeeds
            .Where(g => g.FeedId == feedId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(g => g.Title, newTitle));
    }

    public async Task UpdateGroupFeedDescriptionAsync(FeedId feedId, string newDescription)
    {
        await this.Context.GroupFeeds
            .Where(g => g.FeedId == feedId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(g => g.Description, newDescription));
    }

    public async Task MarkGroupFeedDeletedAsync(FeedId feedId)
    {
        await this.Context.GroupFeeds
            .Where(g => g.FeedId == feedId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(g => g.IsDeleted, true));
    }

    // ===== Key Rotation Operations (FEAT-010) =====

    public async Task<int?> GetMaxKeyGenerationAsync(FeedId feedId) =>
        await this.Context.GroupFeedKeyGenerations
            .Where(k => k.FeedId == feedId)
            .MaxAsync(k => (int?)k.KeyGeneration);

    public async Task<IReadOnlyList<string>> GetActiveGroupMemberAddressesAsync(FeedId feedId) =>
        await this.Context.GroupFeedParticipants
            .Where(p => p.FeedId == feedId
                && p.LeftAtBlock == null
                && p.ParticipantType != ParticipantType.Banned)
            .Select(p => p.ParticipantPublicAddress)
            .ToListAsync();

    public async Task CreateKeyRotationAsync(GroupFeedKeyGenerationEntity keyGeneration) =>
        await this.Context.GroupFeedKeyGenerations.AddAsync(keyGeneration);

    public async Task UpdateCurrentKeyGenerationAsync(FeedId feedId, int newKeyGeneration)
    {
        await this.Context.GroupFeeds
            .Where(g => g.FeedId == feedId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(g => g.CurrentKeyGeneration, newKeyGeneration));
    }
}

