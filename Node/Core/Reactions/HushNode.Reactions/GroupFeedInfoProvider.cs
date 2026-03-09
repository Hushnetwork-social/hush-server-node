using System.Numerics;
using HushShared.Feeds.Model;
using HushNode.Feeds.Storage;
using HushNode.Reactions.Crypto;

namespace HushNode.Reactions;

/// <summary>
/// Implementation of IFeedInfoProvider for reaction targets backed by feed messages.
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
    /// For the current Protocol Omega rollout, this derives a deterministic EC point
    /// from the FeedId for any existing feed type that supports feed-message reactions.
    /// Note: Full implementation with HKDF key derivation from the feed AES key
    /// will be added when client-side key derivation is integrated.
    /// </summary>
    public async Task<ECPoint?> GetFeedPublicKeyAsync(FeedId feedId)
    {
        // Verify the feed exists. Reactions currently operate on feed-message backed targets,
        // which includes direct chats as well as group-style feeds.
        var feed = await _feedsStorage.GetFeedByIdAsync(feedId);
        if (feed != null)
        {
            return DeriveDeterministicPoint(feedId);
        }

        // FEAT-087: social-post reactions currently use PostId as the reaction scope id.
        // Private posts inherit the privacy boundary from the single audience circle that
        // backs the post, so resolve the effective feed from that circle when possible.
        var socialPost = await _feedsStorage.GetSocialPostAsync(feedId.Value);
        var circleFeedId = socialPost?.AudienceCircles
            .Select(x => x.CircleFeedId)
            .FirstOrDefault();

        if (circleFeedId == null)
        {
            return null;
        }

        return DeriveDeterministicPoint(circleFeedId.Value);
    }

    /// <summary>
    /// Gets the author commitment for a specific message.
    /// The author commitment is a 32-byte Poseidon hash of the author's user_secret,
    /// used in ZK proofs to bind reactions to specific authors.
    /// </summary>
    public async Task<byte[]?> GetAuthorCommitmentAsync(FeedMessageId messageId)
    {
        var message = await _feedMessageStorage.GetFeedMessageByIdAsync(messageId);
        if (message?.AuthorCommitment != null)
        {
            return message.AuthorCommitment;
        }

        var socialPost = await _feedsStorage.GetSocialPostAsync(messageId.Value);
        return socialPost?.AuthorCommitment;
    }

    private ECPoint DeriveDeterministicPoint(FeedId feedId)
    {
        var feedIdBytes = feedId.Value.ToByteArray();
        var feedIdScalar = new BigInteger(feedIdBytes, isUnsigned: true, isBigEndian: true);
        var feedHash = _poseidon.Hash(feedIdScalar);

        var scalar = feedHash % _curve.Order;
        if (scalar < 0) scalar += _curve.Order;

        return _curve.ScalarMul(_curve.Generator, scalar);
    }
}
