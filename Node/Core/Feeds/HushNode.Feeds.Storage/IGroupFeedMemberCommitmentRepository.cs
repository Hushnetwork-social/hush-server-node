using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

/// <summary>
/// Repository for Group Feed member commitments.
/// Used by Protocol Omega for anonymous reactions in Group Feeds.
/// </summary>
public interface IGroupFeedMemberCommitmentRepository : IRepository
{
    /// <summary>
    /// Registers a new member commitment for a Group Feed.
    /// </summary>
    Task RegisterCommitmentAsync(
        FeedId feedId,
        byte[] userCommitment,
        int keyGeneration,
        BlockIndex registeredAtBlock);

    /// <summary>
    /// Revokes a member commitment (e.g., when member leaves or is banned).
    /// </summary>
    Task RevokeCommitmentAsync(
        FeedId feedId,
        byte[] userCommitment,
        BlockIndex revokedAtBlock);

    /// <summary>
    /// Gets all active (non-revoked) commitments for a Group Feed.
    /// </summary>
    Task<IEnumerable<GroupFeedMemberCommitment>> GetActiveCommitmentsAsync(FeedId feedId);

    /// <summary>
    /// Checks if a commitment is active for a Group Feed.
    /// </summary>
    Task<bool> IsCommitmentActiveAsync(FeedId feedId, byte[] userCommitment);

    /// <summary>
    /// Gets the key generation for an active commitment.
    /// </summary>
    Task<int?> GetCommitmentKeyGenerationAsync(FeedId feedId, byte[] userCommitment);
}
