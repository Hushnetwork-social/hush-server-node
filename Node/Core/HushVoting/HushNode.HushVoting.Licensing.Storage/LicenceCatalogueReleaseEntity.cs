namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// One append-only catalogue release ledger row. Audit metadata only: runtime plan
/// truth stays in FEAT-012's immutable snapshot. The first release captures the
/// singleton rollout watermark block height used for lazy-migration provenance.
/// </summary>
public sealed class LicenceCatalogueReleaseEntity
{
    public Guid LicenceCatalogueReleaseId { get; set; }

    /// <summary>Stable catalogue version (e.g. hushvoting-licence-catalogue/v1.0.0).</summary>
    public string CatalogueVersion { get; set; } = string.Empty;

    /// <summary>Uppercase hex SHA-256 of the release manifest.</summary>
    public string ReleaseDigestSha256 { get; set; } = string.Empty;

    /// <summary>Schema/contract version of the catalogue (e.g. hushvoting-licence-catalogue/v1).</summary>
    public string SchemaVersion { get; set; } = string.Empty;

    /// <summary>Server release that installed this release (provenance, no identity).</summary>
    public string InstalledByServerRelease { get; set; } = string.Empty;

    /// <summary>Server host that installed this release (provenance, no identity).</summary>
    public string InstalledByServerHost { get; set; } = string.Empty;

    public DateTime InstalledAtUtc { get; set; }

    public bool IsCurrent { get; set; }

    /// <summary>
    /// Authoritative indexed block height captured when the licensing model is first
    /// initialized (the rollout watermark). Set on the first release row only.
    /// </summary>
    public long? RolloutWatermarkBlockHeight { get; set; }
}
