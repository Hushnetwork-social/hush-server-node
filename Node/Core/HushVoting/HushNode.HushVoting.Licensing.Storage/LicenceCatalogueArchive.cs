namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Approved immutable releases retained by the host. Version is an immutable key: changing
/// terms under an existing version is forbidden. Missing history fails closed, never falls
/// back to the current release. Host loading must validate each release before constructing this.
/// </summary>
public sealed class LicenceCatalogueArchive
{
    private readonly IReadOnlyDictionary<string, LicenceServiceConfiguration> _releases;

    public LicenceCatalogueArchive(IEnumerable<LicenceServiceConfiguration> approvedReleases)
    {
        ArgumentNullException.ThrowIfNull(approvedReleases);
        _releases = approvedReleases.ToDictionary(r => r.CatalogueVersion, StringComparer.Ordinal);
    }

    public LicenceServiceConfiguration? Find(string version, string? digest = null) =>
        _releases.TryGetValue(version, out var release)
        && (digest is null || string.Equals(digest, release.ReleaseDigestSha256, StringComparison.Ordinal))
            ? release : null;
}
