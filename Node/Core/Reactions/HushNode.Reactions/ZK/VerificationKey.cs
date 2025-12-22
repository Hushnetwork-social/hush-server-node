using HushNode.Reactions.Crypto;

namespace HushNode.Reactions.ZK;

/// <summary>
/// Groth16 verification key for a specific circuit version.
/// </summary>
public class VerificationKey
{
    /// <summary>
    /// Alpha point (G1).
    /// </summary>
    public required ECPoint Alpha { get; set; }

    /// <summary>
    /// Beta point (G2) - stored as 2 G1 points for pairing.
    /// </summary>
    public required ECPoint[] Beta { get; set; }

    /// <summary>
    /// Gamma point (G2) - stored as 2 G1 points for pairing.
    /// </summary>
    public required ECPoint[] Gamma { get; set; }

    /// <summary>
    /// Delta point (G2) - stored as 2 G1 points for pairing.
    /// </summary>
    public required ECPoint[] Delta { get; set; }

    /// <summary>
    /// Input commitment points (IC) - one per public input.
    /// </summary>
    public required ECPoint[] IC { get; set; }

    /// <summary>
    /// Circuit version identifier.
    /// </summary>
    public required string Version { get; set; }
}
