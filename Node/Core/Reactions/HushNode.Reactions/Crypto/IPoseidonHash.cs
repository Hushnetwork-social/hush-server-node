using System.Numerics;

namespace HushNode.Reactions.Crypto;

/// <summary>
/// Interface for Poseidon hash function used in ZK circuits.
/// </summary>
public interface IPoseidonHash
{
    /// <summary>
    /// Computes the Poseidon hash of the given inputs.
    /// Uses t=3 for 2 inputs, t=5 for 4 inputs.
    /// </summary>
    BigInteger Hash(params BigInteger[] inputs);

    /// <summary>
    /// Computes the Poseidon hash of two field elements.
    /// </summary>
    BigInteger Hash2(BigInteger a, BigInteger b);

    /// <summary>
    /// Computes the Poseidon hash of four field elements.
    /// Used for nullifier computation: Poseidon(user_secret, message_id, feed_id, DOMAIN).
    /// </summary>
    BigInteger Hash4(BigInteger a, BigInteger b, BigInteger c, BigInteger d);
}
