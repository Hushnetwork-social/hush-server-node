using HushShared.HushVoting.Licensing.Model;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Immutable configuration the internal entitlement service needs at runtime. Values are typed
/// primitives sourced by the host composition layer (Phase 6) from the FEAT-012 immutable snapshot
/// and release digest metadata; the module never pulls the host singleton itself and never copies
/// plan truth into editable database data.
/// </summary>
public sealed record LicenceServiceConfiguration
{
    public const string ErrorInvalidCatalogueVersion = "invalid_catalogue_version";
    public const string ErrorInvalidReleaseDigest = "invalid_release_digest";
    public const string ErrorInvalidSchemaVersion = "invalid_schema_version";
    public const string ErrorMissingDirectFreePlan = "catalogue_missing_direct_free";

    private LicenceServiceConfiguration(
        string catalogueVersion,
        string releaseDigestSha256,
        string schemaVersion,
        HushVotingLicenceCatalogue catalogue)
    {
        CatalogueVersion = catalogueVersion;
        ReleaseDigestSha256 = releaseDigestSha256;
        SchemaVersion = schemaVersion;
        Catalogue = catalogue;
    }

    /// <summary>Stable catalogue version (e.g. hushvoting-licence-catalogue/v1.0.0).</summary>
    public string CatalogueVersion { get; }

    /// <summary>Uppercase hex SHA-256 of the configured release manifest (64 chars).</summary>
    public string ReleaseDigestSha256 { get; }

    /// <summary>Schema/contract version of the configured catalogue.</summary>
    public string SchemaVersion { get; }

    /// <summary>Current immutable FEAT-012 catalogue snapshot used for plan lookups and decisions.</summary>
    public HushVotingLicenceCatalogue Catalogue { get; }

    public static bool TryCreate(
        string? catalogueVersion,
        string? releaseDigestSha256,
        string? schemaVersion,
        HushVotingLicenceCatalogue? catalogue,
        out LicenceServiceConfiguration? configuration,
        out string? stableErrorCode)
    {
        configuration = null;
        stableErrorCode = null;

        var version = HushVotingLicenceCatalogueVersion.TryGetKnown(catalogueVersion);
        if (version is null || catalogue is null || catalogue.Version != version)
        {
            stableErrorCode = ErrorInvalidCatalogueVersion;
            return false;
        }

        var digest = releaseDigestSha256?.Trim();
        if (digest is null || digest.Length != 64 || !IsUpperHex(digest))
        {
            stableErrorCode = ErrorInvalidReleaseDigest;
            return false;
        }

        if (string.IsNullOrWhiteSpace(schemaVersion)
            || System.Text.Encoding.UTF8.GetByteCount(schemaVersion.Trim()) > 64)
        {
            stableErrorCode = ErrorInvalidSchemaVersion;
            return false;
        }

        if (catalogue.FindPlan(HushVotingLicencePlanId.DirectFree) is null)
        {
            stableErrorCode = ErrorMissingDirectFreePlan;
            return false;
        }

        configuration = new LicenceServiceConfiguration(
            version.Value,
            digest,
            schemaVersion.Trim(),
            catalogue);
        return true;
    }

    /// <summary>Builds a deterministic test/default configuration over the canonical v1 catalogue.</summary>
    public static LicenceServiceConfiguration CreateDefault(
        string? releaseDigestSha256 = null,
        HushVotingLicenceCatalogue? catalogue = null)
    {
        var snapshot = catalogue ?? HushVotingLicenceCatalogueV1.CreateCatalogue();
        var digest = releaseDigestSha256 ?? new string('A', 64);
        var schema = HushVotingLicenceCatalogueVersion.V1SchemaId;
        if (!TryCreate(snapshot.Version.Value, digest, schema, snapshot, out var configuration, out var error)
            || configuration is null)
        {
            throw new InvalidOperationException($"Default licence configuration invalid: {error}");
        }

        return configuration;
    }

    private static bool IsUpperHex(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c) || char.IsLower(c))
            {
                return false;
            }
        }

        return true;
    }
}
