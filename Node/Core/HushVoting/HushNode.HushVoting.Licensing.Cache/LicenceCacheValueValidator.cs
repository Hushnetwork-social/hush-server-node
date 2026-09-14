using System.Text;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Validates one stored Redis entry as a whole: size/schema via the codec, authentication with the
/// key version named by the envelope, catalogue-token binding, and absolute expiry. Any invalid
/// signal rejects the entire entry as a complete corrupt miss (never partially salvaged or coerced).
/// </summary>
public sealed class LicenceCacheValueValidator
{
    private readonly LicenceCacheEnvelopeCodec _codec;
    private readonly LicenceCacheOptions _options;

    public LicenceCacheValueValidator(
        LicenceCacheEnvelopeCodec codec,
        LicenceCacheOptions options)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Validates a read entry. Returns the validated envelope or a bounded reject reason. The auth key
    /// is chosen by the envelope's key id so only the configured current/previous key can authenticate.
    /// </summary>
    public bool TryValidate(
        string fullRedisKey,
        byte[] envelopeBytes,
        byte[] tagBytes,
        string expectedCatalogueToken,
        LicenceCacheKeyRing keyRing,
        DateTime nowUtc,
        out CachedEntitlementEnvelope? envelope,
        out string? stableReason)
    {
        envelope = null;
        stableReason = null;

        if (!_codec.TryDeserialize(envelopeBytes, out var parsed, out stableReason))
        {
            return false;
        }

        envelope = parsed;

        // Pick the authentication key by the version named inside the envelope (current or previous).
        byte[]? authKey = null;
        if (string.Equals(parsed!.KeyId, keyRing.Current.KeyId, StringComparison.Ordinal))
        {
            authKey = LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(keyRing.Current.SecretBytes);
        }
        else if (keyRing.Previous is not null &&
                 string.Equals(parsed.KeyId, keyRing.Previous.KeyId, StringComparison.Ordinal))
        {
            authKey = LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(keyRing.Previous.SecretBytes);
        }

        if (authKey is null)
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeWrongKeyId;
            return false;
        }

        if (!_codec.VerifyAuthentication(fullRedisKey, envelopeBytes, tagBytes, authKey))
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeUnauthenticated;
            return false;
        }

        // Catalogue binding: an entry from another release namespace is unreachable/never trusted.
        if (!string.Equals(parsed.CatalogueToken, expectedCatalogueToken, StringComparison.Ordinal))
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeWrongCatalogue;
            return false;
        }

        // Absolute expiry: a read never extends validity; both boundaries are independently enforced.
        if (nowUtc >= parsed.CacheValidUntilUtc)
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeExpired;
            return false;
        }

        if (parsed.ExpiresAtUtc is { } assignmentExpiry && nowUtc >= assignmentExpiry)
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeExpired;
            return false;
        }

        return true;
    }
}
