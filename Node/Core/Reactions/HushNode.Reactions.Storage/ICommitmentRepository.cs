using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;

namespace HushNode.Reactions.Storage;

public interface ICommitmentRepository : IRepository
{
    Task<bool> IsCommitmentRegisteredAsync(FeedId feedId, byte[] userCommitment);

    Task AddCommitmentAsync(FeedMemberCommitment commitment);

    Task<int> GetCommitmentIndexAsync(FeedId feedId, byte[] userCommitment);

    Task<IEnumerable<FeedMemberCommitment>> GetCommitmentsForFeedAsync(FeedId feedId);
}
