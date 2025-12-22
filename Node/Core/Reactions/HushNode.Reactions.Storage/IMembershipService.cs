using HushShared.Feeds.Model;
using HushShared.Reactions.Model;

namespace HushNode.Reactions.Storage;

/// <summary>
/// Service for managing feed membership and Merkle proofs.
/// </summary>
public interface IMembershipService
{
    /// <summary>
    /// Gets a Merkle proof for a user's membership in a feed.
    /// </summary>
    Task<MembershipProofResult> GetMembershipProofAsync(FeedId feedId, byte[] userCommitment);

    /// <summary>
    /// Registers a user's commitment in a feed.
    /// </summary>
    Task<RegisterCommitmentResult> RegisterCommitmentAsync(FeedId feedId, byte[] userCommitment);

    /// <summary>
    /// Checks if a commitment is registered in a feed.
    /// </summary>
    Task<bool> IsCommitmentRegisteredAsync(FeedId feedId, byte[] userCommitment);

    /// <summary>
    /// Gets recent Merkle roots for grace period verification.
    /// </summary>
    Task<IEnumerable<MerkleRootHistory>> GetRecentRootsAsync(FeedId feedId, int count);

    /// <summary>
    /// Recalculates and saves the current Merkle root for a feed.
    /// </summary>
    Task<byte[]> UpdateMerkleRootAsync(FeedId feedId, long blockHeight);
}

/// <summary>
/// Result of a membership proof request.
/// </summary>
public class MembershipProofResult
{
    public bool IsMember { get; set; }
    public byte[]? MerkleRoot { get; set; }
    public byte[][]? PathElements { get; set; }
    public int[]? PathIndices { get; set; }
    public int TreeDepth { get; set; }
    public long RootBlockHeight { get; set; }
    public string? ErrorMessage { get; set; }

    public static MembershipProofResult NotMember() =>
        new() { IsMember = false };

    public static MembershipProofResult Success(
        byte[] merkleRoot,
        byte[][] pathElements,
        int[] pathIndices,
        int treeDepth,
        long blockHeight) =>
        new()
        {
            IsMember = true,
            MerkleRoot = merkleRoot,
            PathElements = pathElements,
            PathIndices = pathIndices,
            TreeDepth = treeDepth,
            RootBlockHeight = blockHeight
        };

    public static MembershipProofResult Error(string message) =>
        new() { IsMember = false, ErrorMessage = message };
}

/// <summary>
/// Result of registering a commitment.
/// </summary>
public class RegisterCommitmentResult
{
    public bool Success { get; set; }
    public bool AlreadyRegistered { get; set; }
    public byte[]? MerkleRoot { get; set; }
    public int LeafIndex { get; set; }
    public string? ErrorMessage { get; set; }

    public static RegisterCommitmentResult Ok(byte[] merkleRoot, int leafIndex) =>
        new() { Success = true, MerkleRoot = merkleRoot, LeafIndex = leafIndex };

    public static RegisterCommitmentResult AlreadyExists() =>
        new() { Success = true, AlreadyRegistered = true };

    public static RegisterCommitmentResult Error(string message) =>
        new() { Success = false, ErrorMessage = message };
}
