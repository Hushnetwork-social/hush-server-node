using System.Text;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Privacy-preserving, cluster-safe Redis key construction for licence-cache projection and fill-lease
/// entries. Keys reveal no plan, assignment, revision, raw identity, or commercial state.
///
/// Shape: <c>&lt;instance-prefix&gt;hushvoting:licence-entitlement:v1:&lt;catalogue-token&gt;:&lt;key-id&gt;:{&lt;subject-digest&gt;}:projection</c>
/// and the equivalent <c>:fill-lease</c>. The Redis hash tag <c>{&lt;subject-digest&gt;}</c> keeps one
/// subject's projection and lease in the same cluster slot.
/// </summary>
public static class LicenceCacheKeyBuilder
{
    public const string KeyRoot = "hushvoting:licence-entitlement:v1";
    public const string ProjectionSuffix = "projection";
    public const string FillLeaseSuffix = "fill-lease";
    public const int DigestHexLength = 64;

    /// <summary>
    /// Builds the catalogue token from the FEAT-012 catalogue version and SHA-256 release digest.
    /// Binds every key to one immutable catalogue release; release changes switch namespaces without
    /// any Redis scan or per-subject fan-out.
    /// </summary>
    public static string BuildCatalogueToken(string catalogueVersion, string catalogueDigestSha256)
    {
        if (string.IsNullOrWhiteSpace(catalogueVersion))
        {
            throw new ArgumentException("Catalogue version is required.", nameof(catalogueVersion));
        }

        if (catalogueDigestSha256 is null ||
            catalogueDigestSha256.Length != DigestHexLength ||
            catalogueDigestSha256.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new ArgumentException("Catalogue digest must be a 64-character SHA-256 hex value.",
                nameof(catalogueDigestSha256));
        }

        return $"{catalogueVersion}:{catalogueDigestSha256.ToLowerInvariant()}";
    }

    public static string BuildProjectionKey(
        string instancePrefix,
        string catalogueToken,
        string keyId,
        byte[] subjectDigest) =>
        BuildKey(instancePrefix, catalogueToken, keyId, subjectDigest, ProjectionSuffix);

    public static string BuildFillLeaseKey(
        string instancePrefix,
        string catalogueToken,
        string keyId,
        byte[] subjectDigest) =>
        BuildKey(instancePrefix, catalogueToken, keyId, subjectDigest, FillLeaseSuffix);

    private static string BuildKey(
        string instancePrefix,
        string catalogueToken,
        string keyId,
        byte[] subjectDigest,
        string suffix)
    {
        if (subjectDigest is null || subjectDigest.Length != 32)
        {
            throw new ArgumentException("Subject digest must be 32 bytes.", nameof(subjectDigest));
        }

        if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > 64)
        {
            throw new ArgumentException("Invalid key id.", nameof(keyId));
        }

        var digestHex = Convert.ToHexString(subjectDigest).ToLowerInvariant();
        var prefix = string.IsNullOrEmpty(instancePrefix) ? string.Empty : instancePrefix;
        return $"{prefix}{KeyRoot}:{catalogueToken}:{keyId}:{{{digestHex}}}:{suffix}";
    }

    /// <summary>Exposes the digest in its canonical hex form (test/assert helpers only; never logged).</summary>
    public static string ToDigestHex(byte[] subjectDigest) =>
        Convert.ToHexString(subjectDigest).ToLowerInvariant();
}
