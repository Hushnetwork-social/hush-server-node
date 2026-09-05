namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Bounded stable reason codes for cache envelope/key validation and cache read rejection. Codes are
/// safe for logs, metrics, and errors: they never contain identity, key material, or payload data.
/// </summary>
public static class LicenceCacheReasonCodes
{
    // Key-ring validation
    public const string MissingCurrentKey = "cache_keyring_missing_current";
    public const string WeakCurrentKey = "cache_keyring_current_entropy_too_low";
    public const string WeakPreviousKey = "cache_keyring_previous_entropy_too_low";
    public const string DuplicateKeyId = "cache_keyring_duplicate_key_id";
    public const string InvalidKeyId = "cache_keyring_invalid_key_id";
    public const string PreviousNotOlder = "cache_keyring_previous_must_be_older";
    public const string OverlapExceedsLimit = "cache_keyring_overlap_exceeds_limit";
    public const string InvalidRotationStart = "cache_keyring_invalid_rotation_start";

    // Envelope rejection (complete-miss reasons; never partially salvaged)
    public const string EnvelopeOversized = "cache_envelope_oversized";
    public const string EnvelopeMalformed = "cache_envelope_malformed";
    public const string EnvelopeUnauthenticated = "cache_envelope_unauthenticated";
    public const string EnvelopeWrongSchema = "cache_envelope_wrong_schema";
    public const string EnvelopeWrongKeyId = "cache_envelope_wrong_key_id";
    public const string EnvelopeUnknownField = "cache_envelope_unknown_field";
    public const string EnvelopeDuplicateField = "cache_envelope_duplicate_field";
    public const string EnvelopeWrongCatalogue = "cache_envelope_wrong_catalogue";
    public const string EnvelopeUnsafeProjection = "cache_envelope_unsafe_projection";
    public const string EnvelopeInvalidDates = "cache_envelope_invalid_dates";
    public const string EnvelopeInvalidRevision = "cache_envelope_invalid_revision";
    public const string EnvelopeExpired = "cache_envelope_expired";
    public const string EnvelopeNoPositiveLifetime = "cache_envelope_no_positive_lifetime";

    // CAS outcomes (Phase 3)
    public const string StaleRevisionWrite = "stale_revision_write";
    public const string SameRevisionDivergence = "same_revision_divergence";
}
