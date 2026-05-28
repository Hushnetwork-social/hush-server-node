using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static partial class ProductionLikeOperationalRunArtifactGenerator
{
    private static JsonObject BuildPackage(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        IReadOnlyCollection<ProductionLikeOperationalRunGeneratedArtifact> artifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "production-like-operational-run-package.v1",
            ["packageId"] = "FEAT154-PRODUCTION-LIKE-OPERATIONAL-RUN-PACKAGE",
            ["sourceId"] = ProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["featureSlice"] = ProductionLikeOperationalRunContracts.FeatureId,
            ["generatedAt"] = ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["canonicalizationVersion"] = ProductionLikeOperationalRunContracts.CanonicalizationVersion,
            ["baselineRegister"] = ProductionLikeOperationalRunContracts.Clone(source["baselineRegister"]),
            ["runProfile"] = ProductionLikeOperationalRunContracts.Clone(source["runProfile"]),
            ["dataScope"] = ProductionLikeOperationalRunContracts.Clone(source["dataScope"]),
            ["gateEvaluation"] = GateEvaluationToJson(gate, packageFailures),
            ["artifactPlan"] = ProductionLikeOperationalRunContracts.ToJsonArray(RequiredArtifactPaths),
            ["artifactRefsGeneratedBeforePackage"] = ArtifactRefs(artifacts),
            ["scoreProposal"] = new JsonObject
            {
                ["dimensionId"] = ProductionLikeOperationalRunContracts.DimensionId,
                ["scoreChangeAllowed"] = IsScoreMovementAllowed(gate, packageStatus),
                ["promotionOwner"] = ProductionLikeOperationalRunContracts.PromotionOwner,
                ["directRegisterMutation"] = false,
            },
            ["publicSafety"] = ProductionLikeOperationalRunContracts.Clone(source["publicSafety"]),
            ["residualRisks"] = ProductionLikeOperationalRunContracts.Clone(source["residualRisks"]),
        };

    private static JsonObject BuildPackageHashCurrentnessSummary(
        JsonObject source,
        string packageStatus,
        IReadOnlyCollection<ProductionLikeOperationalRunGeneratedArtifact> artifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "production-like-operational-run-package-hash-currentness-summary.v1",
            ["summaryId"] = "FEAT154-PACKAGE-HASH-CURRENTNESS-SUMMARY",
            ["sourceId"] = ProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["canonicalizationVersion"] = ProductionLikeOperationalRunContracts.CanonicalizationVersion,
            ["generatedArtifactHashes"] = ArtifactRefs(artifacts),
            ["selfHashPolicy"] = "This summary records every generated artifact available before this summary; it excludes itself to avoid a circular hash.",
        };

    private static JsonObject BuildManifest(
        JsonObject source,
        string packageStatus,
        IReadOnlyCollection<ProductionLikeOperationalRunGeneratedArtifact> artifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "production-like-operational-run-manifest.v1",
            ["manifestId"] = "FEAT154-PRODUCTION-LIKE-OPERATIONAL-RUN-MANIFEST",
            ["sourceId"] = ProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["canonicalizationVersion"] = ProductionLikeOperationalRunContracts.CanonicalizationVersion,
            ["artifacts"] = ArtifactRefs(artifacts),
            ["requiredArtifactPaths"] = ProductionLikeOperationalRunContracts.ToJsonArray(RequiredArtifactPaths),
            ["selfHashPolicy"] = "The manifest excludes its own mutable hash field and does not list production-like-operational-run-manifest.json as a hashed child artifact.",
        };

    private static JsonObject BuildGroupSummary(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        string schemaVersion,
        string summaryId,
        string groupName,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = schemaVersion,
            ["summaryId"] = summaryId,
            ["sourceId"] = ProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["gateStatus"] = gate.Status,
            ["sourceGroup"] = groupName,
            ["source"] = ProductionLikeOperationalRunContracts.Clone(source[groupName]),
            ["blockers"] = ProductionLikeOperationalRunContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
            ["limitations"] = ProductionLikeOperationalRunContracts.ToJsonArray(gate.Limitations),
            ["diagnostics"] = ProductionLikeOperationalRunContracts.ToJsonArray(gate.Diagnostics),
        };

    private static JsonObject GateEvaluationToJson(
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures) =>
        new()
        {
            ["status"] = gate.Status,
            ["scoreProposalCanBeGenerated"] = gate.ScoreProposalCanBeGenerated,
            ["scoreChangeAllowed"] = gate.ScoreChangeAllowed,
            ["directRegisterMutation"] = gate.DirectRegisterMutation,
            ["productionRolloutInputStatus"] = gate.ProductionRolloutInputStatus,
            ["promotionRegisterInputStatus"] = gate.PromotionRegisterInputStatus,
            ["blockers"] = ProductionLikeOperationalRunContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
            ["limitations"] = ProductionLikeOperationalRunContracts.ToJsonArray(gate.Limitations),
            ["diagnostics"] = ProductionLikeOperationalRunContracts.ToJsonArray(gate.Diagnostics),
            ["packageFailures"] = ProductionLikeOperationalRunContracts.ToJsonArray(packageFailures),
        };

    private static JsonArray ArtifactRefs(IReadOnlyCollection<ProductionLikeOperationalRunGeneratedArtifact> artifacts) =>
        new(artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(ArtifactRef)
            .ToArray<JsonNode?>());

    private static IReadOnlyList<string> MergeBlockers(
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures) =>
        gate.Blockers
            .Concat(packageFailures)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static bool IsScoreMovementAllowed(
        ProductionLikeOperationalRunGateEvaluation gate,
        string packageStatus) =>
        packageStatus is "accepted" or "accepted_with_limitations" &&
        gate.ScoreProposalCanBeGenerated &&
        gate.ScoreChangeAllowed &&
        !gate.DirectRegisterMutation;
}
