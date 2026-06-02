namespace DeploymentRollbackRehearsalPromoter;

public sealed record DeploymentRollbackRehearsalPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string PublicProofPackagesRoot)
{
    public const string SourceFolder = "Deployment-Rollback-Rehearsal";
    public const string SourceFileName = "deployment-rollback-rehearsal-source.json";
    public const string SourceSchemaFileName = "deployment-rollback-rehearsal-source.schema.json";
    public const string PackageManifestSchemaFileName = "deployment-rollback-rehearsal-package-manifest.schema.json";
    public const string PackageRelativeRoot = "rehearsals/deployment-rollback-emergency/DRR-REHEARSAL-20260602-001";
    public const string SecondCeremonyId = "DPC-REHEARSAL-20260602-002";
    public const string SecondCeremonyRelativeRoot = "ceremonies/" + SecondCeremonyId;

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);
    public string DefaultPackageRoot => Path.Combine(PublicProofPackagesRoot, PackageRelativeRoot);
    public string DefaultSecondCeremonyRoot => Path.Combine(PublicProofPackagesRoot, SecondCeremonyRelativeRoot);

    public static DeploymentRollbackRehearsalPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(fullRoot, SourceFolder);

        return new DeploymentRollbackRehearsalPromotionPaths(
            fullRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            Path.Combine(sourceRoot, "examples"),
            Path.Combine(fullRoot, "Deployment-Proof-Packages"));
    }
}
