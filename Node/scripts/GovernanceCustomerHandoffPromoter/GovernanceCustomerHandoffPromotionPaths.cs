namespace GovernanceCustomerHandoffPromoter;

public sealed record GovernanceCustomerHandoffPromotionPaths(
    string WorkspaceRoot,
    string PublicRepositoryRoot,
    string SchemasRoot,
    string CatalogsRoot,
    string ExamplesRoot,
    string PackagesRoot,
    string RestrictedEvidenceRoot)
{
    public const string SourceFileName = "governance-customer-handoff-source.json";
    public const string SourceSchemaFileName = "governance-customer-handoff-source.schema.json";
    public const string PackageManifestSchemaFileName = "governance-customer-handoff-package-manifest.schema.json";
    public const string ResponsibilityDomainCatalogFileName = "responsibility-domain-catalog.json";
    public const string NonClaimCatalogFileName = "non-claim-catalog.json";
    public const string ExternalPrerequisiteRoutingCatalogFileName = "external-prerequisite-routing-catalog.json";
    public const string ResultCodeCatalogFileName = "result-code-catalog.json";
    public const string NegativeFixtureCatalogFileName = "governance-customer-handoff-negative-fixtures.json";
    public const string PackageFamilyFolder = "governance-customer-handoff";
    public const string DefaultHandoffRunId = "FEAT166-GOVERNANCE-CUSTOMER-HANDOFF-20260603-001";

    public string DefaultSourcePath => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);

    public static GovernanceCustomerHandoffPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var publicRepo = Path.Combine(root, "Governance-Customer-Handoff");
        return new GovernanceCustomerHandoffPromotionPaths(
            root,
            publicRepo,
            Path.Combine(publicRepo, "schemas"),
            Path.Combine(publicRepo, "catalogs"),
            Path.Combine(publicRepo, "examples"),
            Path.Combine(publicRepo, "packages"),
            Path.Combine(
                root,
                "hush-documents",
                "PrivateServer_ElectronicVoting",
                "Governance-Customer-Handoff",
                DefaultHandoffRunId));
    }
}
