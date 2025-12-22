using HushShared.Feeds.Model;

namespace HushShared.Reactions.Model;

/// <summary>
/// Anonymous user commitments for ZK proofs.
/// CRITICAL: This table intentionally has NO link to participant identity.
/// The server cannot determine which commitment belongs to which user.
/// See FAQ Section 12 for privacy design.
/// </summary>
public record FeedMemberCommitment(
    FeedId FeedId,
    byte[] UserCommitment,      // 32 bytes (Poseidon hash of user_secret)
    DateTime RegisteredAt);
