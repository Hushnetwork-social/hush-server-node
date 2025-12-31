using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

/// <summary>
/// Repository implementation for Group Feed member commitments.
/// </summary>
public class GroupFeedMemberCommitmentRepository
    : RepositoryBase<FeedsDbContext>, IGroupFeedMemberCommitmentRepository
{
    public async Task RegisterCommitmentAsync(
        FeedId feedId,
        byte[] userCommitment,
        int keyGeneration,
        BlockIndex registeredAtBlock)
    {
        var commitment = new GroupFeedMemberCommitment(
            Id: 0,  // Auto-generated
            FeedId: feedId,
            UserCommitment: userCommitment,
            KeyGeneration: keyGeneration,
            RegisteredAtBlock: registeredAtBlock,
            RevokedAtBlock: null);

        await this.Context.GroupFeedMemberCommitments.AddAsync(commitment);
    }

    public async Task RevokeCommitmentAsync(
        FeedId feedId,
        byte[] userCommitment,
        BlockIndex revokedAtBlock)
    {
        var commitment = await this.Context.GroupFeedMemberCommitments
            .FirstOrDefaultAsync(c =>
                c.FeedId == feedId &&
                c.UserCommitment.SequenceEqual(userCommitment) &&
                c.RevokedAtBlock == null);

        if (commitment != null)
        {
            // EF Core doesn't track record types well for updates,
            // so we need to use raw SQL or attach a new instance
            this.Context.Entry(commitment).CurrentValues.SetValues(
                commitment with { RevokedAtBlock = revokedAtBlock });
        }
    }

    public async Task<IEnumerable<GroupFeedMemberCommitment>> GetActiveCommitmentsAsync(FeedId feedId) =>
        await this.Context.GroupFeedMemberCommitments
            .Where(c => c.FeedId == feedId && c.RevokedAtBlock == null)
            .ToListAsync();

    public async Task<bool> IsCommitmentActiveAsync(FeedId feedId, byte[] userCommitment) =>
        await this.Context.GroupFeedMemberCommitments
            .AnyAsync(c =>
                c.FeedId == feedId &&
                c.UserCommitment.SequenceEqual(userCommitment) &&
                c.RevokedAtBlock == null);

    public async Task<int?> GetCommitmentKeyGenerationAsync(FeedId feedId, byte[] userCommitment)
    {
        var commitment = await this.Context.GroupFeedMemberCommitments
            .FirstOrDefaultAsync(c =>
                c.FeedId == feedId &&
                c.UserCommitment.SequenceEqual(userCommitment) &&
                c.RevokedAtBlock == null);

        return commitment?.KeyGeneration;
    }
}
