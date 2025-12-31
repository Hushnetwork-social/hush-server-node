using System.Numerics;
using HushShared.Feeds.Model;
using HushNode.Feeds.Storage;
using HushNode.Reactions.Crypto;

namespace HushNode.Reactions;

/// <summary>
/// Implementation of IFeedInfoProvider for Group Feeds.
/// Provides feed public key and author commitment for Protocol Omega ZK verification.
/// </summary>
public class GroupFeedInfoProvider : IFeedInfoProvider
{
    private readonly IFeedMessageStorageService _feedMessageStorage;
    private readonly IFeedsStorageService _feedsStorage;
    private readonly IBabyJubJub _curve;
    private readonly IPoseidonHash _poseidon;

    public GroupFeedInfoProvider(
        IFeedMessageStorageService feedMessageStorage,
        IFeedsStorageService feedsStorage,
        IBabyJubJub curve,
        IPoseidonHash poseidon)
    {
        _feedMessageStorage = feedMessageStorage;
        _feedsStorage = feedsStorage;
        _curve = curve;
        _poseidon = poseidon;
    }

    /// <summary>
    /// Gets the feed public key for ZK proof verification.
    /// For Group Feeds, this derives a deterministic EC point from the FeedId.
    /// Note: Full implementation with HKDF key derivation from Group AES key
    /// will be added when client-side key derivation is integrated.
    /// </summary>
    public async Task<ECPoint?> GetFeedPublicKeyAsync(FeedId feedId)
    {
        // Verify the feed exists and is a Group Feed
        var groupFeed = await _feedsStorage.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return null;
        }

        // Derive a deterministic feed public key from FeedId using Poseidon hash
        // This creates a consistent public key that all members will use
        // The key is derived as: FeedPk = ScalarMul(Hash(FeedId), Generator)
        //
        // TODO: In a future enhancement, this will use HKDF with the Group AES key:
        // ReactionKey = HKDF(GroupAesKey, FeedId, "protocol_omega/group_reaction/v1")
        // FeedPk = ScalarMul(ReactionKey mod Order, Generator)
        //
        // For now, we use a simpler deterministic derivation from FeedId
        // which is sufficient for the verification flow.
        var feedIdBytes = feedId.Value.ToByteArray();
        var feedIdScalar = new BigInteger(feedIdBytes, isUnsigned: true, isBigEndian: true);
        var feedHash = _poseidon.Hash(feedIdScalar);

        // Reduce modulo curve order to get valid scalar
        var scalar = feedHash % _curve.Order;
        if (scalar < 0) scalar += _curve.Order;

        return _curve.ScalarMul(_curve.Generator, scalar);
    }

    /// <summary>
    /// Gets the author commitment for a specific message.
    /// The author commitment is a 32-byte Poseidon hash of the author's user_secret,
    /// used in ZK proofs to bind reactions to specific authors.
    /// </summary>
    public async Task<byte[]?> GetAuthorCommitmentAsync(FeedMessageId messageId)
    {
        var message = await _feedMessageStorage.GetFeedMessageByIdAsync(messageId);
        return message?.AuthorCommitment;
    }
}
