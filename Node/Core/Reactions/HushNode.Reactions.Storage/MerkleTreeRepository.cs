using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;

namespace HushNode.Reactions.Storage;

public class MerkleTreeRepository : RepositoryBase<ReactionsDbContext>, IMerkleTreeRepository
{
    public async Task<IEnumerable<byte[]>> GetCommitmentsAsync(FeedId feedId) =>
        await this.Context.FeedMemberCommitments
            .Where(x => x.FeedId == feedId)
            .OrderBy(x => x.RegisteredAt)
            .Select(x => x.UserCommitment)
            .ToListAsync();

    public async Task<int> GetCommitmentCountAsync(FeedId feedId) =>
        await this.Context.FeedMemberCommitments
            .CountAsync(x => x.FeedId == feedId);

    public async Task<IEnumerable<MerkleRootHistory>> GetRecentRootsAsync(FeedId feedId, int count) =>
        await this.Context.MerkleRootHistories
            .Where(x => x.FeedId == feedId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(count)
            .ToListAsync();

    public async Task SaveRootAsync(MerkleRootHistory root) =>
        await this.Context.MerkleRootHistories.AddAsync(root);

    public async Task<bool> IsRootValidAsync(FeedId feedId, byte[] merkleRoot, int gracePeriodRoots)
    {
        // Get the N most recent roots and check if the provided root matches any of them
        var recentRoots = await this.Context.MerkleRootHistories
            .Where(x => x.FeedId == feedId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(gracePeriodRoots)
            .Select(x => x.MerkleRoot)
            .ToListAsync();

        return recentRoots.Any(r => r.SequenceEqual(merkleRoot));
    }
}
