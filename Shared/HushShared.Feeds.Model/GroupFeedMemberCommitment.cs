using HushShared.Blockchain.BlockModel;

namespace HushShared.Feeds.Model;

/// <summary>
/// Group Feed member commitment for Protocol Omega anonymous reactions.
/// Links a user's ZK commitment to a specific KeyGeneration.
///
/// PRIVACY DESIGN: UserCommitment is a Poseidon hash that cannot be linked to identity.
/// The server cannot determine which participant owns which commitment.
/// </summary>
public record GroupFeedMemberCommitment(
    int Id,
    FeedId FeedId,
    byte[] UserCommitment,       // 32 bytes (Poseidon hash of user_secret)
    int KeyGeneration,           // Which key generation this commitment is valid for
    BlockIndex RegisteredAtBlock,
    BlockIndex? RevokedAtBlock = null);  // Null = active, non-null = revoked
