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

    public async Task<IEnumerable<GroupFeed>> RetrieveGroupFeedsForAddress(
        string publicSigningAddress,
        BlockIndex blockIndex)
    {
        // For group feeds, we use the participant's JoinedAtBlock for the filter.
        // This ensures that when a user is added to an existing group, they will
        // see the group in their next sync (since JoinedAtBlock >= blockIndex).
        //
        // The query returns groups where:
        // 1. User is an active participant (not left, not banned, not blocked)
        // 2. User's JoinedAtBlock is greater than the requested block index
        //    (meaning they joined after the client's last sync position)
        //
        // EF Core translates BlockIndex comparisons via the value converter,
        // so we use the BlockIndex object directly (not .Value).
        return await this.Context.GroupFeeds
            .Include(g => g.Participants)
            .Include(g => g.KeyGenerations)
                .ThenInclude(kg => kg.EncryptedKeys)
            .Where(g =>
                !g.IsDeleted &&
                g.Participants.Any(p =>
                    p.ParticipantPublicAddress == publicSigningAddress &&
                    p.LeftAtBlock == null &&
                    p.ParticipantType != ParticipantType.Banned &&
                    p.ParticipantType != ParticipantType.Blocked &&
                    p.JoinedAtBlock > blockIndex))
            .ToListAsync();
    }

    public async Task<Feed?> GetFeedByIdAsync(FeedId feedId) =>
        await this.Context.Feeds
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.FeedId == feedId);

    public async Task<IReadOnlyList<FeedId>> GetFeedIdsForUserAsync(string publicAddress)
    {
        // Get feed IDs from regular feeds (Personal, Chat)
        var regularFeedIds = await this.Context.FeedParticipants
            .Where(fp => fp.ParticipantPublicAddress == publicAddress)
            .Select(fp => fp.FeedId)
            .Distinct()
            .ToListAsync();

        // Get feed IDs from group feeds (where user is active participant)
        var groupFeedIds = await this.Context.GroupFeedParticipants
            .Where(gp => gp.ParticipantPublicAddress == publicAddress &&
                         gp.LeftAtBlock == null &&
                         gp.ParticipantType != ParticipantType.Banned &&
                         gp.ParticipantType != ParticipantType.Blocked)
            .Select(gp => gp.FeedId)
            .Distinct()
            .ToListAsync();

        // Combine and return unique feed IDs
        return regularFeedIds.Union(groupFeedIds).Distinct().ToList();
    }

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

    // ===== Group Feed Join/Leave Operations (FEAT-008) =====

    public async Task AddParticipantAsync(FeedId feedId, GroupFeedParticipantEntity participant)
    {
        await this.Context.GroupFeedParticipants.AddAsync(participant);
    }

    public async Task UpdateParticipantLeaveStatusAsync(FeedId feedId, string publicAddress, BlockIndex leftAtBlock)
    {
        await this.Context.GroupFeedParticipants
            .Where(p => p.FeedId == feedId && p.ParticipantPublicAddress == publicAddress)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.LeftAtBlock, leftAtBlock)
                .SetProperty(p => p.LastLeaveBlock, leftAtBlock));
    }

    public async Task UpdateParticipantRejoinAsync(FeedId feedId, string publicAddress, BlockIndex joinedAtBlock, ParticipantType participantType)
    {
        await this.Context.GroupFeedParticipants
            .Where(p => p.FeedId == feedId && p.ParticipantPublicAddress == publicAddress)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.LeftAtBlock, (BlockIndex?)null)
                .SetProperty(p => p.JoinedAtBlock, joinedAtBlock)
                .SetProperty(p => p.ParticipantType, participantType));
    }

    public async Task<GroupFeedParticipantEntity?> GetParticipantWithHistoryAsync(FeedId feedId, string publicAddress) =>
        await this.Context.GroupFeedParticipants
            .FirstOrDefaultAsync(p =>
                p.FeedId == feedId &&
                p.ParticipantPublicAddress == publicAddress);

    public async Task<IReadOnlyList<GroupFeedParticipantEntity>> GetActiveParticipantsAsync(FeedId feedId) =>
        await this.Context.GroupFeedParticipants
            .Where(p =>
                p.FeedId == feedId &&
                p.LeftAtBlock == null &&
                p.ParticipantType != ParticipantType.Banned)
            .ToListAsync();

    public async Task AddKeyGenerationAsync(FeedId feedId, GroupFeedKeyGenerationEntity keyGeneration)
    {
        // Add the key generation entity
        await this.Context.GroupFeedKeyGenerations.AddAsync(keyGeneration);

        // Increment the group's CurrentKeyGeneration
        await this.Context.GroupFeeds
            .Where(g => g.FeedId == feedId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(g => g.CurrentKeyGeneration, keyGeneration.KeyGeneration));
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

    // ===== Group Messaging Operations (FEAT-011) =====

    public async Task<GroupFeedKeyGenerationEntity?> GetKeyGenerationByNumberAsync(FeedId feedId, int keyGeneration) =>
        await this.Context.GroupFeedKeyGenerations
            .FirstOrDefaultAsync(k => k.FeedId == feedId && k.KeyGeneration == keyGeneration);

    public async Task<bool> CanMemberSendMessagesAsync(FeedId feedId, string publicAddress) =>
        await this.Context.GroupFeedParticipants
            .AnyAsync(p =>
                p.FeedId == feedId &&
                p.ParticipantPublicAddress == publicAddress &&
                p.LeftAtBlock == null &&
                (p.ParticipantType == ParticipantType.Admin || p.ParticipantType == ParticipantType.Member));

    // ===== Group Feed Query Operations (FEAT-017) =====

    public async Task<IReadOnlyList<GroupFeedKeyGenerationEntity>> GetKeyGenerationsForUserAsync(FeedId feedId, string publicAddress) =>
        await this.Context.GroupFeedKeyGenerations
            .Include(kg => kg.EncryptedKeys)
            .Where(kg =>
                kg.FeedId == feedId &&
                kg.EncryptedKeys.Any(ek => ek.MemberPublicAddress == publicAddress))
            .OrderBy(kg => kg.KeyGeneration)
            .ToListAsync();

    public async Task UpdateFeedBlockIndexAsync(FeedId feedId, BlockIndex blockIndex)
    {
        await this.Context.Feeds
            .Where(f => f.FeedId == feedId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.BlockIndex, blockIndex));
    }
}

