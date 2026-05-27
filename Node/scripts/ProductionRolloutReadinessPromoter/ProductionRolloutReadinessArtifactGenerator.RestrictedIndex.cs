using System.Text.Json.Nodes;

namespace ProductionRolloutReadinessPromoter;

public static partial class ProductionRolloutReadinessArtifactGenerator
{
    private static JsonObject BuildRestrictedReviewerIndex(
        JsonObject source,
        JsonObject artifactAudit,
        DateTimeOffset generatedAt)
    {
        var restrictedRefs = new JsonArray();
        foreach (var reference in ProductionRolloutReadinessContracts.RequireArray(source, "restrictedEvidenceRefs").OfType<JsonObject>())
        {
            restrictedRefs.Add(new JsonObject
            {
                ["refId"] = ProductionRolloutReadinessContracts.GetString(reference, "refId"),
                ["visibility"] = ProductionRolloutReadinessContracts.GetString(reference, "visibility"),
                ["restrictedRef"] = ProductionRolloutReadinessContracts.GetString(reference, "restrictedRef"),
                ["sha256Hash"] = ProductionRolloutReadinessContracts.GetString(reference, "sha256Hash"),
                ["payloadCopied"] = false,
            });
        }

        foreach (var artifact in ProductionRolloutReadinessContracts.RequireArray(artifactAudit, "artifacts").OfType<JsonObject>())
        {
            var restrictedRef = ProductionRolloutReadinessContracts.GetString(artifact, "restrictedRef");
            if (string.IsNullOrWhiteSpace(restrictedRef))
            {
                continue;
            }

            restrictedRefs.Add(new JsonObject
            {
                ["refId"] = ProductionRolloutReadinessContracts.GetString(artifact, "evidenceId"),
                ["visibility"] = ProductionRolloutReadinessContracts.GetString(artifact, "visibility"),
                ["restrictedRef"] = restrictedRef,
                ["sha256Hash"] = ProductionRolloutReadinessContracts.GetString(artifact, "expectedSha256Hash"),
                ["auditResult"] = ProductionRolloutReadinessContracts.GetString(artifact, "auditResult"),
                ["payloadCopied"] = false,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "production-rollout-restricted-reviewer-index.v1",
            ["indexId"] = "FEAT148-PRODUCTION-ROLLOUT-RESTRICTED-REVIEWER-INDEX-001",
            ["sourceId"] = ProductionRolloutReadinessContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionRolloutReadinessContracts.FormatTimestamp(generatedAt),
            ["payloadPolicy"] = "Restricted reviewer index stores refs and hashes only. Private payload bodies are not copied.",
            ["restrictedPayloadsExcluded"] = true,
            ["restrictedRefs"] = new JsonArray(restrictedRefs
                .OfType<JsonObject>()
                .GroupBy(item => ProductionRolloutReadinessContracts.GetString(item, "refId"), StringComparer.Ordinal)
                .Select(group => group.First().DeepClone().AsObject())
                .OrderBy(item => ProductionRolloutReadinessContracts.GetString(item, "refId"), StringComparer.Ordinal)
                .ToArray<JsonNode?>()),
        };
    }
}
