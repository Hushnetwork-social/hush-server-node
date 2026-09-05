using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Strict canonical envelope for one cached entitlement projection. The envelope is a closed schema
/// and is authenticated against its full Redis key (value-authentication subkey), preventing a valid
/// payload from being moved to another subject, catalogue, environment prefix, or purpose.
/// </summary>
public sealed class CachedEntitlementEnvelope
{
    public const string SchemaId = "hushvoting/licence-cache/envelope/v1";

    public required string KeyId { get; init; }
    public required string CatalogueVersion { get; init; }
    public required string CatalogueToken { get; init; }
    public required DateTime CacheWrittenUtc { get; init; }
    public required DateTime CacheValidUntilUtc { get; init; }

    // Client-safe projection fields (same surface as CachedEntitlementProjection).
    public required string PlanId { get; init; }
    public required string PlanFamily { get; init; }
    public required int UpgradeRank { get; init; }
    public int? EligibleVoterCap { get; init; }
    public required bool UnlimitedElections { get; init; }
    public required string TermKind { get; init; }
    public required int TermYears { get; init; }
    public required IReadOnlyList<string> AllowedGovernanceOptionIds { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public required long EntitlementRevision { get; init; }
}

/// <summary>
/// Canonical serializer/deserializer for <see cref="CachedEntitlementEnvelope"/> with duplicate-field
/// rejection, closed-schema enforcement, a 16 KiB bound, and HMAC authentication over the full Redis
/// key and canonical bytes (the tag itself is excluded from the authenticated bytes).
/// </summary>
public sealed class LicenceCacheEnvelopeCodec
{
    private static readonly IReadOnlySet<string> AllowedFields =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "schema", "keyId", "catalogueVersion", "catalogueToken",
            "cacheWrittenUtc", "cacheValidUntilUtc",
            "planId", "planFamily", "upgradeRank", "eligibleVoterCap", "unlimitedElections",
            "termKind", "termYears", "allowedGovernanceOptionIds", "expiresAtUtc", "entitlementRevision",
        };

    /// <summary>Serializes the envelope to canonical UTF-8 bytes (deterministic property order).</summary>
    public byte[] SerializeCanonical(CachedEntitlementEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", CachedEntitlementEnvelope.SchemaId);
            writer.WriteString("keyId", envelope.KeyId);
            writer.WriteString("catalogueVersion", envelope.CatalogueVersion);
            writer.WriteString("catalogueToken", envelope.CatalogueToken);
            writer.WriteString("cacheWrittenUtc", envelope.CacheWrittenUtc);
            writer.WriteString("cacheValidUntilUtc", envelope.CacheValidUntilUtc);
            writer.WriteString("planId", envelope.PlanId);
            writer.WriteString("planFamily", envelope.PlanFamily);
            writer.WriteNumber("upgradeRank", envelope.UpgradeRank);
            if (envelope.EligibleVoterCap is { } cap)
            {
                writer.WriteNumber("eligibleVoterCap", cap);
            }
            else
            {
                writer.WriteNull("eligibleVoterCap");
            }

            writer.WriteBoolean("unlimitedElections", envelope.UnlimitedElections);
            writer.WriteString("termKind", envelope.TermKind);
            writer.WriteNumber("termYears", envelope.TermYears);
            writer.WriteStartArray("allowedGovernanceOptionIds");
            foreach (var option in envelope.AllowedGovernanceOptionIds)
            {
                writer.WriteStringValue(option);
            }

            writer.WriteEndArray();
            if (envelope.ExpiresAtUtc is { } expiresAt)
            {
                writer.WriteString("expiresAtUtc", expiresAt);
            }
            else
            {
                writer.WriteNull("expiresAtUtc");
            }

            writer.WriteNumber("entitlementRevision", envelope.EntitlementRevision);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>Computes the authentication tag over the full Redis key and the canonical envelope bytes.</summary>
    public byte[] ComputeAuthenticationTag(
        string fullRedisKey,
        byte[] canonicalEnvelopeBytes,
        byte[] valueAuthenticationKey)
    {
        ArgumentNullException.ThrowIfNull(fullRedisKey);
        ArgumentNullException.ThrowIfNull(canonicalEnvelopeBytes);
        ArgumentNullException.ThrowIfNull(valueAuthenticationKey);

        using var hmac = new HMACSHA256(valueAuthenticationKey);
        var keyBytes = Encoding.UTF8.GetBytes(fullRedisKey);
        hmac.TransformBlock(keyBytes, 0, keyBytes.Length, null, 0);
        hmac.TransformFinalBlock(canonicalEnvelopeBytes, 0, canonicalEnvelopeBytes.Length);
        return hmac.Hash!;
    }

    /// <summary>
    /// Formats the Redis value: base64(canonical envelope bytes) + "." + lowercase-hex tag.
    /// Neither segment contains '.', so a trailing split is unambiguous.
    /// </summary>
    public string FormatRedisValue(byte[] canonicalEnvelopeBytes, byte[] tagBytes) =>
        Convert.ToBase64String(canonicalEnvelopeBytes) + "." + Convert.ToHexString(tagBytes).ToLowerInvariant();

    /// <summary>
    /// Splits a stored Redis value into canonical envelope bytes and tag bytes.
    /// Returns false with a bounded reason when the value is malformed or oversized.
    /// </summary>
    public bool TrySplitRedisValue(
        string? redisValue,
        int maxEnvelopeBytes,
        out byte[] canonicalEnvelopeBytes,
        out byte[] tagBytes,
        out string? stableReason)
    {
        canonicalEnvelopeBytes = Array.Empty<byte>();
        tagBytes = Array.Empty<byte>();
        stableReason = null;

        if (string.IsNullOrEmpty(redisValue))
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return false;
        }

        var lastDot = redisValue.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == redisValue.Length - 1)
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return false;
        }

        var b64 = redisValue[..lastDot];
        var tagHex = redisValue[(lastDot + 1)..];

        if (tagHex.Length != 64 || tagHex.Any(c => !Uri.IsHexDigit(c)))
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return false;
        }

        // Base64 length for an envelope of maxEnvelopeBytes (ceil(n/3)*4), plus slack.
        var maxBase64Length = ((maxEnvelopeBytes + 2) / 3) * 4;
        if (b64.Length > maxBase64Length)
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeOversized;
            return false;
        }

        try
        {
            canonicalEnvelopeBytes = Convert.FromBase64String(b64);
            tagBytes = Convert.FromHexString(tagHex);
        }
        catch (FormatException)
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return false;
        }

        if (canonicalEnvelopeBytes.Length > maxEnvelopeBytes)
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeOversized;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Strict canonical deserialization: closed schema, no unknown or duplicate fields, bounded
    /// values, UTC dates, monotonic sanity. Returns the reason code for a complete-miss rejection.
    /// </summary>
    public bool TryDeserialize(
        byte[] canonicalEnvelopeBytes,
        out CachedEntitlementEnvelope? envelope,
        out string? stableReason)
    {
        envelope = null;
        stableReason = null;

        try
        {
            var reader = new Utf8JsonReader(canonicalEnvelopeBytes);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                stableReason = LicenceCacheReasonCodes.EnvelopeMalformed;
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? schema = null;
            string? keyId = null;
            string? catalogueVersion = null;
            string? catalogueToken = null;
            DateTime? cacheWrittenUtc = null;
            DateTime? cacheValidUntilUtc = null;
            string? planId = null;
            string? planFamily = null;
            int? upgradeRank = null;
            int? eligibleVoterCap = null;
            bool? unlimitedElections = null;
            string? termKind = null;
            int? termYears = null;
            List<string>? governanceOptions = null;
            DateTime? expiresAtUtc = null;
            long? entitlementRevision = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    stableReason = LicenceCacheReasonCodes.EnvelopeMalformed;
                    return false;
                }

                var property = reader.GetString()!;
                if (!AllowedFields.Contains(property))
                {
                    stableReason = LicenceCacheReasonCodes.EnvelopeUnknownField;
                    return false;
                }

                if (!seen.Add(property))
                {
                    stableReason = LicenceCacheReasonCodes.EnvelopeDuplicateField;
                    return false;
                }

                switch (property)
                {
                    case "schema":
                        schema = ReadString(ref reader, property, ref stableReason);
                        break;
                    case "keyId":
                        keyId = ReadString(ref reader, property, ref stableReason);
                        break;
                    case "catalogueVersion":
                        catalogueVersion = ReadString(ref reader, property, ref stableReason);
                        break;
                    case "catalogueToken":
                        catalogueToken = ReadString(ref reader, property, ref stableReason);
                        break;
                    case "cacheWrittenUtc":
                        cacheWrittenUtc = ReadUtc(ref reader, property, ref stableReason);
                        break;
                    case "cacheValidUntilUtc":
                        cacheValidUntilUtc = ReadUtc(ref reader, property, ref stableReason);
                        break;
                    case "planId":
                        planId = ReadString(ref reader, property, ref stableReason);
                        break;
                    case "planFamily":
                        planFamily = ReadString(ref reader, property, ref stableReason);
                        break;
                    case "upgradeRank":
                        upgradeRank = ReadInt32(ref reader, property, ref stableReason);
                        break;
                    case "eligibleVoterCap":
                        eligibleVoterCap = ReadNullableInt32(ref reader, property, ref stableReason);
                        break;
                    case "unlimitedElections":
                        unlimitedElections = ReadBoolean(ref reader, property, ref stableReason);
                        break;
                    case "termKind":
                        termKind = ReadString(ref reader, property, ref stableReason);
                        break;
                    case "termYears":
                        termYears = ReadInt32(ref reader, property, ref stableReason);
                        break;
                    case "allowedGovernanceOptionIds":
                        governanceOptions = ReadStringArray(ref reader, property, ref stableReason);
                        break;
                    case "expiresAtUtc":
                        expiresAtUtc = ReadNullableUtc(ref reader, property, ref stableReason);
                        break;
                    case "entitlementRevision":
                        entitlementRevision = ReadInt64(ref reader, property, ref stableReason);
                        break;
                }

                if (stableReason is not null)
                {
                    return false;
                }
            }

            // Required/closed completeness.
            if (schema != CachedEntitlementEnvelope.SchemaId)
            {
                stableReason = LicenceCacheReasonCodes.EnvelopeWrongSchema;
                return false;
            }

            if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > 64)
            {
                stableReason = LicenceCacheReasonCodes.EnvelopeWrongKeyId;
                return false;
            }

            if (string.IsNullOrWhiteSpace(planId) || planId.Length > 64 ||
                string.IsNullOrWhiteSpace(planFamily) || planFamily.Length > 64 ||
                string.IsNullOrWhiteSpace(termKind) || termKind.Length > 32 ||
                string.IsNullOrWhiteSpace(catalogueVersion) || catalogueVersion.Length > 96 ||
                string.IsNullOrWhiteSpace(catalogueToken) || catalogueToken.Length > 192 ||
                governanceOptions is null || governanceOptions.Count > 64 ||
                governanceOptions.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > 64))
            {
                stableReason = LicenceCacheReasonCodes.EnvelopeUnsafeProjection;
                return false;
            }

            if (upgradeRank is < 0 or > 1_000_000 ||
                eligibleVoterCap is < 0 or > int.MaxValue ||
                termYears is < 1 or > 100 ||
                entitlementRevision is < 0)
            {
                stableReason = LicenceCacheReasonCodes.EnvelopeInvalidRevision;
                return false;
            }

            if (cacheWrittenUtc is null || cacheValidUntilUtc is null ||
                expiresAtUtc is { Kind: not DateTimeKind.Utc })
            {
                stableReason = LicenceCacheReasonCodes.EnvelopeInvalidDates;
                return false;
            }

            if (cacheValidUntilUtc <= cacheWrittenUtc)
            {
                stableReason = LicenceCacheReasonCodes.EnvelopeInvalidDates;
                return false;
            }

            envelope = new CachedEntitlementEnvelope
            {
                KeyId = keyId!,
                CatalogueVersion = catalogueVersion!,
                CatalogueToken = catalogueToken!,
                CacheWrittenUtc = cacheWrittenUtc!.Value,
                CacheValidUntilUtc = cacheValidUntilUtc!.Value,
                PlanId = planId!,
                PlanFamily = planFamily!,
                UpgradeRank = upgradeRank!.Value,
                EligibleVoterCap = eligibleVoterCap,
                UnlimitedElections = unlimitedElections ?? false,
                TermKind = termKind!,
                TermYears = termYears!.Value,
                AllowedGovernanceOptionIds = governanceOptions!,
                ExpiresAtUtc = expiresAtUtc,
                EntitlementRevision = entitlementRevision!.Value,
            };
            return true;
        }
        catch (JsonException)
        {
            stableReason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return false;
        }
    }

    /// <summary>Verifies the tag with a constant-time comparison over the full Redis key + envelope bytes.</summary>
    public bool VerifyAuthentication(
        string fullRedisKey,
        byte[] canonicalEnvelopeBytes,
        byte[] expectedTag,
        byte[] valueAuthenticationKey)
    {
        var computed = ComputeAuthenticationTag(fullRedisKey, canonicalEnvelopeBytes, valueAuthenticationKey);
        return CryptographicOperations.FixedTimeEquals(computed, expectedTag);
    }

    // ---- strict primitive readers -------------------------------------------

    private static string? ReadString(ref Utf8JsonReader reader, string property, ref string? reason)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
        {
            reason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return null;
        }

        return reader.GetString();
    }

    private static DateTime? ReadUtc(ref Utf8JsonReader reader, string property, ref string? reason)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
        {
            reason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return null;
        }

        if (!DateTimeOffset.TryParse(reader.GetString(), out var parsed) ||
            parsed.Offset != TimeSpan.Zero)
        {
            reason = LicenceCacheReasonCodes.EnvelopeInvalidDates;
            return null;
        }

        return parsed.UtcDateTime;
    }

    private static DateTime? ReadNullableUtc(ref Utf8JsonReader reader, string property, ref string? reason)
    {
        if (!reader.Read())
        {
            reason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return null;
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            reason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return null;
        }

        if (!DateTimeOffset.TryParse(reader.GetString(), out var parsed) ||
            parsed.Offset != TimeSpan.Zero)
        {
            reason = LicenceCacheReasonCodes.EnvelopeInvalidDates;
            return null;
        }

        return parsed.UtcDateTime;
    }

    private static int? ReadInt32(ref Utf8JsonReader reader, string property, ref string? reason)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var value))
        {
            reason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return null;
        }

        return value;
    }

    private static int? ReadNullableInt32(ref Utf8JsonReader reader, string property, ref string? reason)
    {
        if (!reader.Read())
        {
            reason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return null;
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var value))
        {
            reason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return null;
        }

        return value;
    }

    private static long? ReadInt64(ref Utf8JsonReader reader, string property, ref string? reason)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out var value))
        {
            reason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return null;
        }

        return value;
    }

    private static bool? ReadBoolean(ref Utf8JsonReader reader, string property, ref string? reason)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.True && reader.TokenType != JsonTokenType.False)
        {
            reason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return null;
        }

        return reader.GetBoolean();
    }

    private static List<string>? ReadStringArray(ref Utf8JsonReader reader, string property, ref string? reason)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            reason = LicenceCacheReasonCodes.EnvelopeMalformed;
            return null;
        }

        var list = new List<string>();
        while (true)
        {
            if (!reader.Read())
            {
                reason = LicenceCacheReasonCodes.EnvelopeMalformed;
                return null;
            }

            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                reason = LicenceCacheReasonCodes.EnvelopeMalformed;
                return null;
            }

            list.Add(reader.GetString()!);
        }

        return list;
    }
}
