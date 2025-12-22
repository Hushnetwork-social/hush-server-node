using System.Numerics;

namespace HushNode.Reactions;

/// <summary>
/// Service for computing and managing user commitments for anonymous reactions.
/// A user commitment is Poseidon(userSecret), where userSecret is derived from the user's private key.
/// </summary>
public interface IUserCommitmentService
{
    /// <summary>
    /// Gets the user commitment for the local server user (Stacker).
    /// The commitment is computed from the server's private signing key.
    /// </summary>
    byte[] GetLocalUserCommitment();

    /// <summary>
    /// Gets the user secret for the local server user.
    /// Used for creating ZK proofs.
    /// </summary>
    BigInteger GetLocalUserSecret();

    /// <summary>
    /// Computes a commitment from a user secret.
    /// commitment = Poseidon(userSecret)
    /// </summary>
    byte[] ComputeCommitment(BigInteger userSecret);
}
