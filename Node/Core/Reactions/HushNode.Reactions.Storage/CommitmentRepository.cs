using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;

namespace HushNode.Reactions.Storage;

public class CommitmentRepository : RepositoryBase<ReactionsDbContext>, ICommitmentRepository
{
    public async Task<bool> IsCommitmentRegisteredAsync(FeedId feedId, byte[] userCommitment) =>
        await this.Context.FeedMemberCommitments
            .AnyAsync(x => x.FeedId == feedId && x.UserCommitment == userCommitment);

    public async Task AddCommitmentAsync(FeedMemberCommitment commitment) =>
        await this.Context.FeedMemberCommitments.AddAsync(commitment);

    public async Task<int> GetCommitmentIndexAsync(FeedId feedId, byte[] userCommitment)
    {
        // Get all commitments ordered by registration time
        // and find the index of the given commitment
        var commitments = await this.Context.FeedMemberCommitments
            .Where(x => x.FeedId == feedId)
            .OrderBy(x => x.RegisteredAt)
            .Select(x => x.UserCommitment)
            .ToListAsync();

        for (int i = 0; i < commitments.Count; i++)
        {
            if (commitments[i].SequenceEqual(userCommitment))
            {
                return i;
            }
        }

        return -1; // Not found
    }

    public async Task<IEnumerable<FeedMemberCommitment>> GetCommitmentsForFeedAsync(FeedId feedId) =>
        await this.Context.FeedMemberCommitments
            .Where(x => x.FeedId == feedId)
            .OrderBy(x => x.RegisteredAt)
            .ToListAsync();
}
