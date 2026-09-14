using System.Security.Cryptography;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// One decoded versioned HMAC master key. Master key material is supplied through environment or the
/// deployment secret provider and is never persisted in committed settings or logs.
/// </summary>
public sealed class LicenceCacheMasterKey
{
    private LicenceCacheMasterKey(string keyId, byte[] secretBytes, DateTime rotationStartedUtc)
    {
        KeyId = keyId;
        SecretBytes = secretBytes;
        RotationStartedUtc = rotationStartedUtc;
    }

    /// <summary>Stable non-secret identifier of this key version (bounded length).</summary>
    public string KeyId { get; }

    /// <summary>Raw decoded master key bytes; treated as secret.</summary>
    public byte[] SecretBytes { get; }

    /// <summary>UTC instant this key version became current (used for rotation overlap).</summary>
    public DateTime RotationStartedUtc { get; }

    /// <summary>
    /// Validates and wraps decoded master key material. Returns a stable error code when the key id is
    /// malformed or the decoded entropy is below the configured minimum (never discloses key bytes).
    /// </summary>
    public static LicenceCacheMasterKey Create(
        string keyId,
        byte[] secretBytes,
        DateTime rotationStartedUtc,
        LicenceCacheOptions options,
        out string? stableErrorCode)
    {
        ArgumentNullException.ThrowIfNull(options);
        stableErrorCode = null;

        if (string.IsNullOrWhiteSpace(keyId) ||
            keyId.Length < options.MinKeyIdCharacters ||
            keyId.Length > options.MaxKeyIdCharacters)
        {
            stableErrorCode = LicenceCacheOptionErrorCodes.InvalidKeyId;
            throw new ArgumentException("Invalid key id.", nameof(keyId));
        }

        if (secretBytes is null || secretBytes.Length < options.MinMasterKeyBytes)
        {
            stableErrorCode = LicenceCacheOptionErrorCodes.WeakCurrentKeyEntropy;
            throw new ArgumentException("Master key entropy is below the required minimum.", nameof(secretBytes));
        }

        if (rotationStartedUtc.Kind != DateTimeKind.Utc)
        {
            stableErrorCode = LicenceCacheOptionErrorCodes.InvalidRotationStartedAt;
            throw new ArgumentException("Rotation start must be UTC.", nameof(rotationStartedUtc));
        }

        return new LicenceCacheMasterKey(keyId, (byte[])secretBytes.Clone(), rotationStartedUtc);
    }
}

/// <summary>
/// Validated current + optional previous HMAC key ring. At most one previous key may be configured and
/// its overlap with the current key cannot exceed the configured limit. The previous key cannot remain
/// configured beyond the overlap window; rotation never scans Redis.
/// </summary>
public sealed class LicenceCacheKeyRing
{
    private LicenceCacheKeyRing(LicenceCacheMasterKey current, LicenceCacheMasterKey? previous)
    {
        Current = current;
        Previous = previous;
    }

    public LicenceCacheMasterKey Current { get; }

    /// <summary>Optional previous key active only during the bounded overlap window.</summary>
    public LicenceCacheMasterKey? Previous { get; }

    public bool HasPrevious => Previous is not null;

    /// <summary>
    /// Validates current and optional previous keys: presence, entropy, id bounds/uniqueness,
    /// ordering (previous strictly older), and overlap within the configured limit.
    /// Returns a stable error code or null when the ring is valid.
    /// </summary>
    public static LicenceCacheKeyRing? TryCreate(
        LicenceCacheMasterKey current,
        LicenceCacheMasterKey? previous,
        LicenceCacheOptions options,
        out string? stableErrorCode)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(options);
        stableErrorCode = null;

        if (previous is null)
        {
            return new LicenceCacheKeyRing(current, null);
        }

        if (string.Equals(current.KeyId, previous.KeyId, StringComparison.Ordinal))
        {
            stableErrorCode = LicenceCacheReasonCodes.DuplicateKeyId;
            return null;
        }

        if (previous.RotationStartedUtc >= current.RotationStartedUtc)
        {
            stableErrorCode = LicenceCacheReasonCodes.PreviousNotOlder;
            return null;
        }

        var overlap = current.RotationStartedUtc - previous.RotationStartedUtc;
        if (overlap > TimeSpan.FromDays(options.PreviousKeyOverlapMaxDays))
        {
            stableErrorCode = LicenceCacheReasonCodes.OverlapExceedsLimit;
            return null;
        }

        return new LicenceCacheKeyRing(current, previous);
    }
}
