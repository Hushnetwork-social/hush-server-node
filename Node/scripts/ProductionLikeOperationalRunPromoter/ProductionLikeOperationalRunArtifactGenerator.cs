using System.Text;
using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static partial class ProductionLikeOperationalRunArtifactGenerator
{
    public const string PackagePath = "production-like-operational-run-package.json";
    public const string ManifestPath = "production-like-operational-run-manifest.json";
    public const string RunProfileSummaryPath = "validation/run-profile-summary.json";
    public const string DeploymentProofBindingSummaryPath = "validation/deployment-proof-binding-summary.json";
    public const string MonitoringAlertingSummaryPath = "validation/monitoring-alerting-summary.json";
    public const string SupportOperatorHandoffSummaryPath = "validation/support-operator-handoff-summary.json";
    public const string BackupRestoreSummaryPath = "validation/backup-restore-summary.json";
    public const string IncidentNoIncidentSummaryPath = "validation/incident-no-incident-summary.json";
    public const string SecuritySupportFreshnessSummaryPath = "validation/security-support-freshness-summary.json";
    public const string PostmortemSummaryPath = "validation/postmortem-summary.json";
    public const string PackageHashCurrentnessSummaryPath = "validation/package-hash-currentness-summary.json";
    public const string NoSecretScanResultPath = "validation/no-secret-scan-result.json";
    public const string ReadinessFragmentPath = "readiness/production-like-operational-run-readiness-fragment.json";
    public const string ScoreProposalPath = "readiness/production-like-operational-run-score-proposal.json";
    public const string DownstreamHandoffPath = "handoff/production-like-operational-run-downstream-handoff.json";
    public const string PublicSafeSummaryPath = "public-safe-production-like-operational-run-summary.md";
    public const string RestrictedEvidenceIndexPath = "restricted-production-like-operational-run-evidence-index.md";
    public const string ReadmePath = "README.md";

    public static readonly string[] RequiredArtifactPaths =
    [
        BackupRestoreSummaryPath,
        DeploymentProofBindingSummaryPath,
        DownstreamHandoffPath,
        IncidentNoIncidentSummaryPath,
        ManifestPath,
        MonitoringAlertingSummaryPath,
        NoSecretScanResultPath,
        PackageHashCurrentnessSummaryPath,
        PackagePath,
        PostmortemSummaryPath,
        PublicSafeSummaryPath,
        ReadinessFragmentPath,
        ReadmePath,
        RestrictedEvidenceIndexPath,
        RunProfileSummaryPath,
        ScoreProposalPath,
        SecuritySupportFreshnessSummaryPath,
        SupportOperatorHandoffSummaryPath,
    ];

    public static ProductionLikeOperationalRunGeneratedPackage Generate(
        ProductionLikeOperationalRunPromotionPaths paths,
        string? sourceInput = null,
        DateTimeOffset? generatedAt = null) =>
        GenerateFromSource(
            ProductionLikeOperationalRunContracts.LoadSource(paths, sourceInput),
            generatedAt);

    public static ProductionLikeOperationalRunGeneratedPackage GenerateFromSource(
        JsonObject source,
        DateTimeOffset? generatedAt = null)
    {
        var effectiveGeneratedAt = generatedAt ?? DateTimeOffset.UtcNow;
        var gate = ProductionLikeOperationalRunGateChecker.Evaluate(source);
        var packageStatus = gate.Status;
        var publicSummary = BuildPublicSafeSummary(source, gate, packageStatus, effectiveGeneratedAt);
        var readme = BuildReadme(source, gate, packageStatus, effectiveGeneratedAt);
        var publicFindings = ProductionLikeOperationalRunContracts.ScanPublicOutput(
            source,
            [(PublicSafeSummaryPath, publicSummary), (ReadmePath, readme)]);
        var packageFailures = publicFindings
            .Select(finding => $"FEAT154-PUBLIC-OUTPUT-{finding.Category.ToUpperInvariant()}-{finding.RelativePath}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

        if (packageFailures.Count > 0)
        {
            packageStatus = "blocked";
            publicSummary = BuildPublicSafeSummary(source, gate, packageStatus, effectiveGeneratedAt);
            readme = BuildReadme(source, gate, packageStatus, effectiveGeneratedAt);
        }

        var baseArtifacts = new List<ProductionLikeOperationalRunGeneratedArtifact>
        {
            JsonArtifact(RunProfileSummaryPath, BuildRunProfileSummary(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(DeploymentProofBindingSummaryPath, BuildDeploymentProofBindingSummary(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(MonitoringAlertingSummaryPath, BuildMonitoringAlertingSummary(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(SupportOperatorHandoffSummaryPath, BuildSupportOperatorHandoffSummary(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(BackupRestoreSummaryPath, BuildBackupRestoreSummary(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(IncidentNoIncidentSummaryPath, BuildIncidentNoIncidentSummary(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(SecuritySupportFreshnessSummaryPath, BuildSecuritySupportFreshnessSummary(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(PostmortemSummaryPath, BuildPostmortemSummary(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(NoSecretScanResultPath, BuildNoSecretScanResult(source, publicFindings, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(ScoreProposalPath, BuildScoreProposal(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(DownstreamHandoffPath, BuildDownstreamHandoff(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            TextArtifact(PublicSafeSummaryPath, publicSummary),
            TextArtifact(RestrictedEvidenceIndexPath, BuildRestrictedEvidenceIndex(source, effectiveGeneratedAt)),
            TextArtifact(ReadmePath, readme),
        };

        baseArtifacts.Add(JsonArtifact(
            PackagePath,
            BuildPackage(source, gate, packageFailures, packageStatus, baseArtifacts, effectiveGeneratedAt)));

        var hashCurrentnessArtifact = JsonArtifact(
            PackageHashCurrentnessSummaryPath,
            BuildPackageHashCurrentnessSummary(source, packageStatus, baseArtifacts, effectiveGeneratedAt));

        var manifestArtifact = JsonArtifact(
            ManifestPath,
            BuildManifest(source, packageStatus, baseArtifacts.Append(hashCurrentnessArtifact).ToArray(), effectiveGeneratedAt));

        var artifacts = baseArtifacts
            .Append(hashCurrentnessArtifact)
            .Append(manifestArtifact)
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new ProductionLikeOperationalRunGeneratedPackage(
            packageStatus,
            artifacts,
            gate,
            packageFailures,
            publicFindings);
    }

    private static ProductionLikeOperationalRunGeneratedArtifact JsonArtifact(string relativePath, JsonObject content)
    {
        var text = ProductionLikeOperationalRunContracts.CanonicalJson(content);
        return new ProductionLikeOperationalRunGeneratedArtifact(
            relativePath,
            text,
            ProductionLikeOperationalRunContracts.Sha256Hex(text),
            "application/json");
    }

    private static ProductionLikeOperationalRunGeneratedArtifact TextArtifact(string relativePath, string content)
    {
        var normalized = ProductionLikeOperationalRunContracts.NormalizeLineEndings(content);
        if (!normalized.EndsWith('\n'))
        {
            normalized += "\n";
        }

        return new ProductionLikeOperationalRunGeneratedArtifact(
            relativePath,
            normalized,
            ProductionLikeOperationalRunContracts.Sha256Hex(normalized),
            "text/markdown");
    }

    private static JsonObject ArtifactRef(ProductionLikeOperationalRunGeneratedArtifact artifact) =>
        new()
        {
            ["path"] = artifact.RelativePath,
            ["sha256Hash"] = artifact.Sha256Hash,
            ["hashFormat"] = "sha256-hex",
            ["mediaType"] = artifact.MediaType,
            ["sizeBytes"] = Encoding.UTF8.GetByteCount(artifact.Content),
        };
}
