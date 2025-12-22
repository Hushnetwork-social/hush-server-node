using System.Numerics;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.Storage;

namespace HushNode.Reactions;

/// <summary>
/// Service for managing feed membership and Merkle proofs.
/// Implements Merkle tree operations for ZK proof membership verification.
/// </summary>
public class MembershipService : IMembershipService
{
    private readonly IUnitOfWorkProvider<ReactionsDbContext> _unitOfWorkProvider;
    private readonly IPoseidonHash _poseidon;
    private readonly ILogger<MembershipService> _logger;

    // Merkle tree depth (supports 2^20 = ~1M members per feed)
    private const int TreeDepth = 20;

    // Zero values for empty leaves at each level
    private readonly BigInteger[] _zeroValues;

    public MembershipService(
        IUnitOfWorkProvider<ReactionsDbContext> unitOfWorkProvider,
        IPoseidonHash poseidon,
        ILogger<MembershipService> logger)
    {
        _unitOfWorkProvider = unitOfWorkProvider;
        _poseidon = poseidon;
        _logger = logger;

        // Precompute zero values for each level
        // Zero leaf is just 0, then each level up is Hash(zero[i], zero[i])
        _zeroValues = new BigInteger[TreeDepth + 1];
        _zeroValues[0] = BigInteger.Zero;
        for (int i = 1; i <= TreeDepth; i++)
        {
            _zeroValues[i] = _poseidon.Hash2(_zeroValues[i - 1], _zeroValues[i - 1]);
        }
    }

    public async Task<MembershipProofResult> GetMembershipProofAsync(FeedId feedId, byte[] userCommitment)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var commitmentRepo = unitOfWork.GetRepository<ICommitmentRepository>();
        var merkleRepo = unitOfWork.GetRepository<IMerkleTreeRepository>();

        // Check if commitment is registered
        var isRegistered = await commitmentRepo.IsCommitmentRegisteredAsync(feedId, userCommitment);
        if (!isRegistered)
        {
            return MembershipProofResult.NotMember();
        }

        // Get all commitments for the feed
        var commitments = (await merkleRepo.GetCommitmentsAsync(feedId)).ToList();

        // Find leaf index
        var leafIndex = -1;
        for (int i = 0; i < commitments.Count; i++)
        {
            if (commitments[i].SequenceEqual(userCommitment))
            {
                leafIndex = i;
                break;
            }
        }

        if (leafIndex == -1)
        {
            return MembershipProofResult.NotMember();
        }

        // Build Merkle proof
        var (root, pathElements, pathIndices) = BuildMerkleProof(commitments, leafIndex);

        // Get the most recent root's block height
        var recentRoots = await merkleRepo.GetRecentRootsAsync(feedId, 1);
        var blockHeight = recentRoots.FirstOrDefault()?.BlockHeight ?? 0;

        return MembershipProofResult.Success(
            ToBytes32(root),
            pathElements.Select(ToBytes32).ToArray(),
            pathIndices,
            TreeDepth,
            blockHeight);
    }

    public async Task<RegisterCommitmentResult> RegisterCommitmentAsync(FeedId feedId, byte[] userCommitment)
    {
        _logger.LogDebug("[MembershipService] RegisterCommitmentAsync called for feed {FeedId}", feedId);

        try
        {
            using var unitOfWork = _unitOfWorkProvider.CreateWritable(System.Data.IsolationLevel.Serializable);

            var commitmentRepo = unitOfWork.GetRepository<ICommitmentRepository>();
            var merkleRepo = unitOfWork.GetRepository<IMerkleTreeRepository>();

            // Check if already registered
            var isRegistered = await commitmentRepo.IsCommitmentRegisteredAsync(feedId, userCommitment);

            if (isRegistered)
            {
                return RegisterCommitmentResult.AlreadyExists();
            }

            // Get current commitment count (this will be the new leaf index)
            var leafIndex = await merkleRepo.GetCommitmentCountAsync(feedId);

            // Add the new commitment
            var commitment = new FeedMemberCommitment(
                FeedId: feedId,
                UserCommitment: userCommitment,
                RegisteredAt: DateTime.UtcNow);
            await commitmentRepo.AddCommitmentAsync(commitment);

            // Get all commitments to compute new root
            var allCommitments = (await merkleRepo.GetCommitmentsAsync(feedId)).ToList();
            allCommitments.Add(userCommitment);

            // Compute new Merkle root
            var newRoot = ComputeMerkleRoot(allCommitments);

            // Save the new root
            var rootHistory = new MerkleRootHistory(
                Id: 0,  // Auto-generated
                FeedId: feedId,
                MerkleRoot: ToBytes32(newRoot),
                BlockHeight: 0,  // TODO: Get current block height
                CreatedAt: DateTime.UtcNow);
            await merkleRepo.SaveRootAsync(rootHistory);

            await unitOfWork.CommitAsync();

            _logger.LogInformation("[MembershipService] Registered commitment for feed {FeedId}, leaf index: {LeafIndex}", feedId, leafIndex);

            return RegisterCommitmentResult.Ok(ToBytes32(newRoot), leafIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MembershipService] Failed to register commitment for feed {FeedId}", feedId);
            return RegisterCommitmentResult.Error($"Registration failed: {ex.Message}");
        }
    }

    public async Task<bool> IsCommitmentRegisteredAsync(FeedId feedId, byte[] userCommitment)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<ICommitmentRepository>();
        return await repository.IsCommitmentRegisteredAsync(feedId, userCommitment);
    }

    public async Task<IEnumerable<MerkleRootHistory>> GetRecentRootsAsync(FeedId feedId, int count)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IMerkleTreeRepository>();
        var roots = await repository.GetRecentRootsAsync(feedId, count);

        // If no roots exist but there are commitments, compute and save the root
        if (!roots.Any())
        {
            var commitmentCount = await repository.GetCommitmentCountAsync(feedId);
            if (commitmentCount > 0)
            {
                _logger.LogInformation("[MembershipService] No Merkle roots found but {Count} commitments exist for feed {FeedId}. Computing root...",
                    commitmentCount, feedId);

                // Compute and save root using writable context
                await UpdateMerkleRootAsync(feedId, 0);

                // Re-fetch the roots
                using var readUnitOfWork = _unitOfWorkProvider.CreateReadOnly();
                var readRepo = readUnitOfWork.GetRepository<IMerkleTreeRepository>();
                return await readRepo.GetRecentRootsAsync(feedId, count);
            }
        }

        return roots;
    }

    public async Task<byte[]> UpdateMerkleRootAsync(FeedId feedId, long blockHeight)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable();
        var commitmentRepo = unitOfWork.GetRepository<ICommitmentRepository>();
        var merkleRepo = unitOfWork.GetRepository<IMerkleTreeRepository>();

        // Get all commitments
        var commitments = (await merkleRepo.GetCommitmentsAsync(feedId)).ToList();

        // Compute Merkle root
        var root = ComputeMerkleRoot(commitments);

        // Save the root
        var rootHistory = new MerkleRootHistory(
            Id: 0,
            FeedId: feedId,
            MerkleRoot: ToBytes32(root),
            BlockHeight: blockHeight,
            CreatedAt: DateTime.UtcNow);
        await merkleRepo.SaveRootAsync(rootHistory);

        await unitOfWork.CommitAsync();

        return ToBytes32(root);
    }

    /// <summary>
    /// Computes the Merkle root from a list of leaf commitments.
    /// Uses sparse tree computation - only computes non-zero branches.
    /// </summary>
    private BigInteger ComputeMerkleRoot(List<byte[]> commitments)
    {
        if (commitments.Count == 0)
        {
            // Empty tree - return zero root
            return _zeroValues[TreeDepth];
        }

        // Convert commitments to BigIntegers
        var leaves = commitments
            .Select(c => new BigInteger(c, isUnsigned: true, isBigEndian: true))
            .ToList();

        // Compute sparse Merkle root - only process actual leaves, use precomputed zeros for empty subtrees
        var currentLevel = leaves;

        for (int level = 0; level < TreeDepth; level++)
        {
            var nextLevel = new List<BigInteger>();
            var levelSize = currentLevel.Count;

            for (int i = 0; i < levelSize; i += 2)
            {
                var left = currentLevel[i];
                // If we have a right sibling, use it; otherwise use the zero value for this level
                var right = (i + 1 < levelSize) ? currentLevel[i + 1] : _zeroValues[level];
                nextLevel.Add(_poseidon.Hash2(left, right));
            }

            // If odd number of elements and we need to continue up the tree
            // The last element will hash with the zero value (already handled above)

            currentLevel = nextLevel;

            // If we've reduced to a single element and there are more levels,
            // we need to hash with zero subtrees
            if (currentLevel.Count == 1 && level < TreeDepth - 1)
            {
                // Continue hashing the single root with zero subtrees
                for (int remainingLevel = level + 1; remainingLevel < TreeDepth; remainingLevel++)
                {
                    currentLevel[0] = _poseidon.Hash2(currentLevel[0], _zeroValues[remainingLevel]);
                }
                break;
            }
        }

        return currentLevel[0];
    }

    /// <summary>
    /// Builds a Merkle proof for a specific leaf.
    /// Uses sparse tree computation for efficiency.
    /// </summary>
    private (BigInteger Root, BigInteger[] PathElements, int[] PathIndices) BuildMerkleProof(
        List<byte[]> commitments,
        int leafIndex)
    {
        // Convert commitments to BigIntegers
        var leaves = commitments
            .Select(c => new BigInteger(c, isUnsigned: true, isBigEndian: true))
            .ToList();

        var pathElements = new List<BigInteger>();
        var pathIndices = new List<int>();
        var currentIndex = leafIndex;
        var currentLevel = leaves;

        for (int level = 0; level < TreeDepth; level++)
        {
            // Sibling index
            var siblingIndex = currentIndex ^ 1;

            // Path index indicates if we're the left (0) or right (1) child
            pathIndices.Add(currentIndex & 1);

            // Add sibling to path - use zero value if sibling doesn't exist
            var sibling = siblingIndex < currentLevel.Count
                ? currentLevel[siblingIndex]
                : _zeroValues[level];
            pathElements.Add(sibling);

            // Build next level (sparse - only compute what we have)
            var nextLevel = new List<BigInteger>();
            var levelSize = currentLevel.Count;
            for (int i = 0; i < levelSize; i += 2)
            {
                var left = currentLevel[i];
                var right = (i + 1 < levelSize) ? currentLevel[i + 1] : _zeroValues[level];
                nextLevel.Add(_poseidon.Hash2(left, right));
            }

            // Move to parent index
            currentIndex /= 2;
            currentLevel = nextLevel;

            // If we've reduced to one element, fill remaining path with zero subtrees
            if (currentLevel.Count == 1 && level < TreeDepth - 1)
            {
                for (int remainingLevel = level + 1; remainingLevel < TreeDepth; remainingLevel++)
                {
                    // We're always the left child (index 0) when there's only one element
                    pathIndices.Add(0);
                    pathElements.Add(_zeroValues[remainingLevel]);
                    currentLevel[0] = _poseidon.Hash2(currentLevel[0], _zeroValues[remainingLevel]);
                }
                break;
            }
        }

        return (currentLevel[0], pathElements.ToArray(), pathIndices.ToArray());
    }

    private static byte[] ToBytes32(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length >= 32)
            return bytes[..32];

        var result = new byte[32];
        Array.Copy(bytes, 0, result, 32 - bytes.Length, bytes.Length);
        return result;
    }
}
