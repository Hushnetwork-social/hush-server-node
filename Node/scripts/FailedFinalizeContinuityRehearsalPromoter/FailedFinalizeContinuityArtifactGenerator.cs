using System.Text;
using System.Text.Json.Nodes;

namespace FailedFinalizeContinuityRehearsalPromoter;

public sealed record FailedFinalizeContinuityGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash,
    string ContentType);

public sealed record FailedFinalizeContinuityGeneratedPackage(
    string Status,
    IReadOnlyList<FailedFinalizeContinuityGeneratedArtifact> Artifacts,
    FailedFinalizeContinuityGateEvaluation GateEvaluation,
    FailedFinalizeContinuityReviewerOutputs ReviewerOutputs,
    IReadOnlyList<string> PackageFailures);

public static class FailedFinalizeContinuityArtifactGenerator
{
    public const string SourceEchoPath = "failed-finalize-continuity-source.json";
    public const string PackagePath = "failed-finalize-continuity-package.json";
    public const string ManifestPath = "failed-finalize-continuity-manifest.json";
    public const string CheckResultsPath = "validation/failed-finalize-check-results.json";
    public const string PackageHashValidationPath = "validation/failed-finalize-package-hash-validation.json";
    public const string PublicStatusPath = "public/failed-finalize-status.json";
    public const string PublicSafeSummaryPath = "public/failed-finalize-public-safe-summary.md";
    public const string RestrictedEvidenceIndexPath = "restricted/failed-finalize-restricted-evidence-index.json";
    public const string ReadinessFragmentPath = "readiness/failed-finalize-readiness-fragment.json";
    public const string ScoreProposalPath = "readiness/failed-finalize-score-proposal.json";
    public const string DownstreamHandoffPath = "handoff/failed-finalize-downstream-handoff.json";
    public const string ReadmePath = "README.md";

    public static readonly string[] RequiredArtifactPaths =
    [
        CheckResultsPath,
        DownstreamHandoffPath,
        ManifestPath,
        PackageHashValidationPath,
        PackagePath,
        PublicSafeSummaryPath,
        PublicStatusPath,
        ReadinessFragmentPath,
        ReadmePath,
        RestrictedEvidenceIndexPath,
        ScoreProposalPath,
        SourceEchoPath,
    ];

    public static FailedFinalizeContinuityGeneratedPackage Generate(
        FailedFinalizeContinuityPromotionPaths paths,
        string? sourceInput = null,
        DateTimeOffset? generatedAt = null) =>
        GenerateFromSource(
            FailedFinalizeContinuityContracts.LoadSource(paths, sourceInput),
            generatedAt);

    public static FailedFinalizeContinuityGeneratedPackage GenerateFromSource(
        JsonObject source,
        DateTimeOffset? generatedAt = null)
    {
        var effectiveGeneratedAt = generatedAt ?? DateTimeOffset.UtcNow;
        var gate = FailedFinalizeContinuityGateChecker.Evaluate(source);
        var reviewerOutputs = FailedFinalizeContinuityReviewerOutputGenerator.Generate(source);
        var packageFailures = new List<string>();
        if (!reviewerOutputs.PublicSafetyScan.Passed)
        {
            packageFailures.Add("FEAT155-PUBLIC-SAFETY-SCAN-FAILED");
        }

        if (reviewerOutputs.NoUiBoundary.Status != "confirmed")
        {
            packageFailures.Add("FEAT155-NO-UI-BOUNDARY-BLOCKED");
        }

        var packageStatus = gate.Status == "accepted" && packageFailures.Count == 0
            ? "accepted"
            : "blocked";

        var baseArtifacts = new List<FailedFinalizeContinuityGeneratedArtifact>
        {
            JsonArtifact(SourceEchoPath, source),
            JsonArtifact(CheckResultsPath, BuildCheckResults(source, gate, reviewerOutputs, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(PublicStatusPath, BuildPublicStatus(source, packageStatus, effectiveGeneratedAt)),
            TextArtifact(PublicSafeSummaryPath, reviewerOutputs.PublicSafeSummary),
            TextArtifact(ReadmePath, BuildReadme(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonTextArtifact(RestrictedEvidenceIndexPath, reviewerOutputs.RestrictedEvidenceIndexJson),
            JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(ScoreProposalPath, BuildScoreProposal(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
            JsonArtifact(DownstreamHandoffPath, BuildDownstreamHandoff(source, gate, packageFailures, packageStatus, effectiveGeneratedAt)),
        };

        baseArtifacts.Add(JsonArtifact(
            PackagePath,
            BuildPackage(source, gate, reviewerOutputs, packageFailures, packageStatus, baseArtifacts, effectiveGeneratedAt)));

        var hashValidationArtifact = JsonArtifact(
            PackageHashValidationPath,
            BuildPackageHashValidation(source, packageStatus, baseArtifacts, effectiveGeneratedAt));

        var manifestArtifact = JsonArtifact(
            ManifestPath,
            BuildManifest(source, packageStatus, baseArtifacts.Append(hashValidationArtifact).ToArray(), effectiveGeneratedAt));

        var artifacts = baseArtifacts
            .Append(hashValidationArtifact)
            .Append(manifestArtifact)
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new FailedFinalizeContinuityGeneratedPackage(
            packageStatus,
            artifacts,
            gate,
            reviewerOutputs,
            packageFailures);
    }

    private static JsonObject BuildCheckResults(
        JsonObject source,
        FailedFinalizeContinuityGateEvaluation gate,
        FailedFinalizeContinuityReviewerOutputs reviewerOutputs,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "failed-finalize-check-results.v1",
            ["checkResultId"] = "FEAT155-FAILED-FINALIZE-CHECK-RESULTS",
            ["sourceId"] = FailedFinalizeContinuityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FailedFinalizeContinuityContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["gateStatus"] = gate.Status,
            ["scoreChangeAllowed"] = gate.ScoreChangeAllowed && packageStatus == "accepted",
            ["directRegisterMutation"] = gate.DirectRegisterMutation,
            ["downstreamHandoffStatus"] = gate.DownstreamHandoffStatus,
            ["blockers"] = FailedFinalizeContinuityContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
            ["diagnostics"] = FailedFinalizeContinuityContracts.ToJsonArray(gate.Diagnostics),
            ["publicSafety"] = new JsonObject
            {
                ["passed"] = reviewerOutputs.PublicSafetyScan.Passed,
                ["findings"] = FailedFinalizeContinuityContracts.ToJsonArray(reviewerOutputs.PublicSafetyScan.Findings),
            },
            ["noUiBoundary"] = new JsonObject
            {
                ["status"] = reviewerOutputs.NoUiBoundary.Status,
                ["hasUiChanges"] = reviewerOutputs.NoUiBoundary.HasUiChanges,
                ["changedUiFiles"] = FailedFinalizeContinuityContracts.ToJsonArray(reviewerOutputs.NoUiBoundary.ChangedUiFiles),
                ["note"] = reviewerOutputs.NoUiBoundary.Note,
            },
        };

    private static JsonObject BuildPublicStatus(JsonObject source, string packageStatus, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "failed-finalize-public-status.v1",
            ["statusId"] = "FEAT155-FAILED-FINALIZE-PUBLIC-STATUS",
            ["sourceId"] = FailedFinalizeContinuityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FailedFinalizeContinuityContracts.FormatTimestamp(generatedAt),
            ["packageStatus"] = packageStatus,
            ["outcomeStatus"] = "failed_to_finalize",
            ["noValidOfficialResult"] = true,
            ["cleanFinalizationClaimed"] = false,
            ["publicStateElectionReadinessClaimed"] = false,
            ["productionRolloutReadinessClaimed"] = false,
            ["sourcePublicStatus"] = FailedFinalizeContinuityContracts.Clone(source["publicSafeStatus"]),
        };

    private static JsonObject BuildReadinessFragment(
        JsonObject source,
        FailedFinalizeContinuityGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt)
    {
        var baseline = FailedFinalizeContinuityContracts.TryObject(source, "baselineRegister");
        var currentScore = FailedFinalizeContinuityContracts.GetInt(baseline, "currentScore");
        var targetScore = FailedFinalizeContinuityContracts.GetInt(baseline, "targetScore");
        var scoreAllowed = packageStatus == "accepted" && gate.ScoreChangeAllowed;

        return new JsonObject
        {
            ["schemaVersion"] = "failed-finalize-readiness-fragment.v1",
            ["fragmentId"] = "FEAT155-RDY-DIM-009-READINESS-FRAGMENT",
            ["featureSlice"] = FailedFinalizeContinuityContracts.FeatureId,
            ["sourceId"] = FailedFinalizeContinuityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FailedFinalizeContinuityContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["dimensionId"] = FailedFinalizeContinuityContracts.DimensionId,
            ["directRegisterMutation"] = false,
            ["doesNotMutateRegister"] = true,
            ["promotionOwner"] = FailedFinalizeContinuityContracts.PromotionOwner,
            ["scoreEffect"] = new JsonObject
            {
                ["currentDimensionScore"] = currentScore,
                ["targetDimensionScore"] = targetScore,
                ["appliedDimensionScore"] = scoreAllowed ? targetScore : currentScore,
                ["scoreChangeAllowed"] = scoreAllowed,
                ["scoreChangeBlockedBy"] = FailedFinalizeContinuityContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
            },
            ["residualRisks"] = FailedFinalizeContinuityContracts.Clone(source["residualRisks"]),
        };
    }

    private static JsonObject BuildScoreProposal(
        JsonObject source,
        FailedFinalizeContinuityGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt)
    {
        var proposal = FailedFinalizeContinuityContracts.TryObject(source, "readinessProposal");
        var fromScore = FailedFinalizeContinuityContracts.GetInt(proposal, "proposedScoreFrom");
        var requestedToScore = FailedFinalizeContinuityContracts.GetInt(proposal, "proposedScoreTo");
        var scoreAllowed = packageStatus == "accepted" && gate.ScoreChangeAllowed;

        return new JsonObject
        {
            ["schemaVersion"] = "failed-finalize-score-proposal.v1",
            ["proposalId"] = "FEAT155-RDY-DIM-009-SCORE-PROPOSAL",
            ["sourceId"] = FailedFinalizeContinuityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FailedFinalizeContinuityContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["dimensionId"] = FailedFinalizeContinuityContracts.DimensionId,
            ["proposedScoreFrom"] = fromScore,
            ["proposedScoreTo"] = scoreAllowed ? requestedToScore : fromScore,
            ["requestedScoreTo"] = requestedToScore,
            ["scoreChangeAllowed"] = scoreAllowed,
            ["directRegisterMutation"] = false,
            ["doesNotMutateRegister"] = true,
            ["promotionOwner"] = FailedFinalizeContinuityContracts.PromotionOwner,
            ["blockedBy"] = FailedFinalizeContinuityContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
        };
    }

    private static JsonObject BuildDownstreamHandoff(
        JsonObject source,
        FailedFinalizeContinuityGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt)
    {
        var handoff = FailedFinalizeContinuityContracts.TryObject(source, "downstreamHandoff");

        return new JsonObject
        {
            ["schemaVersion"] = "failed-finalize-downstream-handoff.v1",
            ["handoffId"] = "FEAT155-DOWNSTREAM-HANDOFF",
            ["sourceId"] = FailedFinalizeContinuityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FailedFinalizeContinuityContracts.FormatTimestamp(generatedAt),
            ["featureId"] = FailedFinalizeContinuityContracts.FeatureId,
            ["status"] = packageStatus,
            ["sourcePackageId"] = FailedFinalizeContinuityContracts.GetString(handoff, "sourcePackageId"),
            ["sourcePackageHash"] = FailedFinalizeContinuityContracts.GetString(handoff, "sourcePackageHash"),
            ["consumers"] = FailedFinalizeContinuityContracts.Clone(handoff?["consumers"]) ?? new JsonArray("FEAT-148", "FEAT-156"),
            ["registerPromotionOwner"] = FailedFinalizeContinuityContracts.PromotionOwner,
            ["directRegisterMutation"] = false,
            ["scoreProposalPath"] = ScoreProposalPath,
            ["readinessFragmentPath"] = ReadinessFragmentPath,
            ["failedFinalizeBlockerClearance"] = FailedFinalizeContinuityContracts.Clone(handoff?["blockerEffect"]) ?? new JsonObject(),
            ["blockedBy"] = FailedFinalizeContinuityContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
            ["residualRisks"] = FailedFinalizeContinuityContracts.Clone(handoff?["residualRisks"]) ?? FailedFinalizeContinuityContracts.Clone(source["residualRisks"]),
            ["publicSafety"] = FailedFinalizeContinuityContracts.Clone(handoff?["publicSafety"]) ?? new JsonObject(),
            ["sourceHandoff"] = FailedFinalizeContinuityContracts.Clone(handoff),
        };
    }

    private static JsonObject BuildPackage(
        JsonObject source,
        FailedFinalizeContinuityGateEvaluation gate,
        FailedFinalizeContinuityReviewerOutputs reviewerOutputs,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        IReadOnlyCollection<FailedFinalizeContinuityGeneratedArtifact> artifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "failed-finalize-continuity-package.v1",
            ["packageId"] = "FEAT155-FAILED-FINALIZE-CONTINUITY-PACKAGE",
            ["sourceId"] = FailedFinalizeContinuityContracts.GetString(source, "sourceId"),
            ["featureSlice"] = FailedFinalizeContinuityContracts.FeatureId,
            ["generatedAt"] = FailedFinalizeContinuityContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["canonicalizationVersion"] = FailedFinalizeContinuityContracts.CanonicalizationVersion,
            ["outcomeStatus"] = "failed_to_finalize",
            ["noOfficialResult"] = true,
            ["cleanFinalizationClaimed"] = false,
            ["gateResult"] = BuildCheckResults(source, gate, reviewerOutputs, packageFailures, packageStatus, generatedAt),
            ["artifactRefs"] = ArtifactRefs(artifacts),
            ["downstreamHandoffPath"] = DownstreamHandoffPath,
        };

    private static JsonObject BuildPackageHashValidation(
        JsonObject source,
        string packageStatus,
        IReadOnlyCollection<FailedFinalizeContinuityGeneratedArtifact> artifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "failed-finalize-package-hash-validation.v1",
            ["validationId"] = "FEAT155-PACKAGE-HASH-VALIDATION",
            ["sourceId"] = FailedFinalizeContinuityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FailedFinalizeContinuityContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["canonicalizationVersion"] = FailedFinalizeContinuityContracts.CanonicalizationVersion,
            ["generatedArtifactHashes"] = ArtifactRefs(artifacts),
            ["selfHashPolicy"] = "This validation artifact records every generated artifact except itself to avoid circular hashes.",
        };

    private static JsonObject BuildManifest(
        JsonObject source,
        string packageStatus,
        IReadOnlyCollection<FailedFinalizeContinuityGeneratedArtifact> artifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "failed-finalize-continuity-manifest.v1",
            ["manifestId"] = "FEAT155-FAILED-FINALIZE-CONTINUITY-MANIFEST",
            ["sourceId"] = FailedFinalizeContinuityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FailedFinalizeContinuityContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["artifactCount"] = artifacts.Count,
            ["artifacts"] = ArtifactRefs(artifacts),
        };

    private static string BuildReadme(
        JsonObject source,
        FailedFinalizeContinuityGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt)
    {
        var blockers = MergeBlockers(gate, packageFailures);
        var builder = new StringBuilder();
        builder.AppendLine("# FEAT-155 Failed-Finalize Continuity Package");
        builder.AppendLine();
        builder.AppendLine($"Generated: {FailedFinalizeContinuityContracts.FormatTimestamp(generatedAt)}");
        builder.AppendLine($"Source: {FailedFinalizeContinuityContracts.GetString(source, "sourceId")}");
        builder.AppendLine($"Status: {packageStatus}");
        builder.AppendLine();
        builder.AppendLine("This package records a failed-finalize continuity outcome. No valid official result exists in this package.");
        builder.AppendLine();
        builder.AppendLine("## Non-Claims");
        builder.AppendLine("- Legal remedy sufficiency is not claimed.");
        builder.AppendLine("- Production organizational rollout readiness is not claimed.");
        builder.AppendLine("- Public/state election readiness is not claimed.");
        builder.AppendLine();
        builder.AppendLine("## Handoff");
        builder.AppendLine($"- Score proposal: `{ScoreProposalPath}`");
        builder.AppendLine($"- Readiness fragment: `{ReadinessFragmentPath}`");
        builder.AppendLine($"- Downstream handoff: `{DownstreamHandoffPath}`");
        builder.AppendLine();
        builder.AppendLine("## Blockers");
        if (blockers.Count == 0)
        {
            builder.AppendLine("- None.");
        }
        else
        {
            foreach (var blocker in blockers)
            {
                builder.AppendLine($"- {blocker}");
            }
        }

        return builder.ToString();
    }

    private static JsonArray ArtifactRefs(IReadOnlyCollection<FailedFinalizeContinuityGeneratedArtifact> artifacts) =>
        new(artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact => new JsonObject
            {
                ["path"] = artifact.RelativePath,
                ["sha256Hash"] = artifact.Sha256Hash,
                ["contentType"] = artifact.ContentType,
            })
            .ToArray<JsonNode?>());

    private static IReadOnlyList<string> MergeBlockers(
        FailedFinalizeContinuityGateEvaluation gate,
        IReadOnlyList<string> packageFailures) =>
        gate.Blockers
            .Concat(packageFailures)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static FailedFinalizeContinuityGeneratedArtifact JsonArtifact(string relativePath, JsonObject content)
    {
        var text = FailedFinalizeContinuityContracts.CanonicalJson(content);
        return new FailedFinalizeContinuityGeneratedArtifact(
            relativePath,
            text,
            FailedFinalizeContinuityContracts.Sha256Hex(text),
            "application/json");
    }

    private static FailedFinalizeContinuityGeneratedArtifact JsonTextArtifact(string relativePath, string content)
    {
        var normalized = FailedFinalizeContinuityContracts.NormalizeLineEndings(content);
        if (!normalized.EndsWith('\n'))
        {
            normalized += "\n";
        }

        return new FailedFinalizeContinuityGeneratedArtifact(
            relativePath,
            normalized,
            FailedFinalizeContinuityContracts.Sha256Hex(normalized),
            "application/json");
    }

    private static FailedFinalizeContinuityGeneratedArtifact TextArtifact(string relativePath, string content)
    {
        var normalized = FailedFinalizeContinuityContracts.NormalizeLineEndings(content);
        if (!normalized.EndsWith('\n'))
        {
            normalized += "\n";
        }

        return new FailedFinalizeContinuityGeneratedArtifact(
            relativePath,
            normalized,
            FailedFinalizeContinuityContracts.Sha256Hex(normalized),
            "text/markdown");
    }
}
