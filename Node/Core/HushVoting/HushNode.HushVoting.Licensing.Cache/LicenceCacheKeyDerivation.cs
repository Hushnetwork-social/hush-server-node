using System.Security.Cryptography;
using System.Text;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Domain-separated subkey derivation and subject-digest computation.
///
/// From each versioned master key two independent 32-byte subkeys are derived with HKDF-SHA-256 and
/// fixed versioned context labels:
///   <c>hushvoting/licence-cache/v1/subject-key</c>
///   <c>hushvoting/licence-cache/v1/value-authentication</c>
/// The configured master is never used directly for either purpose.
/// </summary>
public static class LicenceCacheKeyDerivation
{
    public const int SubKeyLengthBytes = 32;
    public const string SubjectKeyContext = "hushvoting/licence-cache/v1/subject-key";
    public const string ValueAuthenticationContext = "hushvoting/licence-cache/v1/value-authentication";

    private static readonly byte[] SubjectKeyInfo = Encoding.UTF8.GetBytes(SubjectKeyContext);
    private static readonly byte[] ValueAuthenticationInfo = Encoding.UTF8.GetBytes(ValueAuthenticationContext);

    /// <summary>Derives the HMAC key used to digest subject identities (never the master itself).</summary>
    public static byte[] DeriveSubjectKey(byte[] masterKeyBytes)
    {
        ArgumentNullException.ThrowIfNull(masterKeyBytes);
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            masterKeyBytes,
            salt: null,
            info: SubjectKeyInfo,
            outputLength: SubKeyLengthBytes);
    }

    /// <summary>Derives the HMAC key used to authenticate cached values against their full Redis key.</summary>
    public static byte[] DeriveValueAuthenticationKey(byte[] masterKeyBytes)
    {
        ArgumentNullException.ThrowIfNull(masterKeyBytes);
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            masterKeyBytes,
            salt: null,
            info: ValueAuthenticationInfo,
            outputLength: SubKeyLengthBytes);
    }

    /// <summary>
    /// Computes the privacy-preserving subject digest: HMAC-SHA-256 over the canonical normalized
    /// public signing address using the derived subject key. The raw address never appears in Redis
    /// keys, logs, metrics, traces, errors, or outbox payloads.
    /// </summary>
    public static byte[] ComputeSubjectDigest(byte[] subjectKey, string canonicalPublicSigningAddress)
    {
        ArgumentNullException.ThrowIfNull(subjectKey);
        if (string.IsNullOrWhiteSpace(canonicalPublicSigningAddress))
        {
            throw new ArgumentException("Canonical address is required.", nameof(canonicalPublicSigningAddress));
        }

        using var hmac = new HMACSHA256(subjectKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPublicSigningAddress));
    }
}
