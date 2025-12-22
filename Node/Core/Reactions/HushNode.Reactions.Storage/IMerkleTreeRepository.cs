using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;

namespace HushNode.Reactions.Storage;

public interface IMerkleTreeRepository : IRepository
{
    Task<IEnumerable<byte[]>> GetCommitmentsAsync(FeedId feedId);

    Task<int> GetCommitmentCountAsync(FeedId feedId);

    Task<IEnumerable<MerkleRootHistory>> GetRecentRootsAsync(FeedId feedId, int count);

    Task SaveRootAsync(MerkleRootHistory root);

    Task<bool> IsRootValidAsync(FeedId feedId, byte[] merkleRoot, int gracePeriodRoots);
}
