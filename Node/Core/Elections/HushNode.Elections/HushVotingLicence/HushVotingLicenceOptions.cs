namespace HushNode.Elections.HushVotingLicence;

/// <summary>Configuration for the release-controlled HushVoting licence catalogue.</summary>
public sealed record HushVotingLicenceOptions
{
    public const string SectionName = "HushVotingLicences";
    public const string DefaultCatalogueRelativePath =
        "licence-catalogues/hushvoting-v1.0.0/approved-licence-catalogue.json";

    public string CatalogueRelativePath { get; init; } = DefaultCatalogueRelativePath;

    public string RequiredCatalogueVersion { get; init; } =
        HushShared.HushVoting.Licensing.Model.HushVotingLicenceCatalogueVersion.V1Value;
}
