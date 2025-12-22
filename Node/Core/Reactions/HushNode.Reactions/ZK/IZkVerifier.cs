using System.Numerics;
using HushNode.Reactions.Crypto;

namespace HushNode.Reactions.ZK;

/// <summary>
/// Interface for ZK proof verification.
/// </summary>
public interface IZkVerifier
{
    /// <summary>
    /// Verifies a Groth16 proof with the given public inputs.
    /// </summary>
    Task<VerifyResult> VerifyAsync(
        byte[] proof,
        PublicInputs inputs,
        string circuitVersion);

    /// <summary>
    /// Gets the current (recommended) circuit version.
    /// </summary>
    string GetCurrentVersion();

    /// <summary>
    /// Checks if a circuit version is supported.
    /// </summary>
    bool IsVersionSupported(string version);

    /// <summary>
    /// Checks if a circuit version is known to be vulnerable.
    /// </summary>
    bool IsVulnerableVersion(string version);
}

/// <summary>
/// Public inputs for the reaction ZK circuit.
/// </summary>
public class PublicInputs
{
    /// <summary>
    /// Nullifier: Poseidon(user_secret, message_id, feed_id, DOMAIN)
    /// </summary>
    public required byte[] Nullifier { get; set; }

    /// <summary>
    /// 6 encrypted ciphertext C1 points (one per emoji type).
    /// </summary>
    public required ECPoint[] CiphertextC1 { get; set; }

    /// <summary>
    /// 6 encrypted ciphertext C2 points (one per emoji type).
    /// </summary>
    public required ECPoint[] CiphertextC2 { get; set; }

    /// <summary>
    /// Message ID being reacted to.
    /// </summary>
    public required byte[] MessageId { get; set; }

    /// <summary>
    /// Feed public key for ElGamal encryption.
    /// </summary>
    public required ECPoint FeedPk { get; set; }

    /// <summary>
    /// Merkle root of the membership tree.
    /// </summary>
    public required byte[] MembersRoot { get; set; }

    /// <summary>
    /// Author commitment from the message.
    /// </summary>
    public required BigInteger AuthorCommitment { get; set; }
}

/// <summary>
/// Result of ZK proof verification.
/// </summary>
public class VerifyResult
{
    public bool Valid { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public string? Warning { get; set; }

    public static VerifyResult Success() => new() { Valid = true };

    public static VerifyResult Failure(string error, string? message = null) =>
        new() { Valid = false, Error = error, Message = message };

    public static VerifyResult SuccessWithWarning(string warning) =>
        new() { Valid = true, Warning = warning };
}
