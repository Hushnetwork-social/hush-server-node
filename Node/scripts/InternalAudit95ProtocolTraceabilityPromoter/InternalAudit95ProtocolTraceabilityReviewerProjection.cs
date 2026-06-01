using System.Text.Json.Nodes;

namespace InternalAudit95ProtocolTraceabilityPromoter;

public static class InternalAudit95ProtocolTraceabilityReviewerProjection
{
    public static string BuildPublicSafeSummary(
        JsonObject source,
        string packageStatus,
        IReadOnlyList<string> blockers,
        DateTimeOffset generatedAt)
    {
        var statusLine = packageStatus == "accepted_candidate"
            ? "The internal checker completed without score-blocking traceability findings."
            : "The internal checker found score-blocking traceability findings.";
        var blockerLine = blockers.Count == 0
            ? "No score-blocking traceability finding is open in this package."
            : "One or more traceability findings remain open in this package.";

        return $"""
            # HushVoting Internal Traceability Summary

            Generated: {InternalAudit95ProtocolTraceabilityContracts.FormatTimestamp(generatedAt)}

            This package is release-bound internal traceability evidence for external-review preparation.

            {statusLine}
            {blockerLine}

            The package maps protocol, readiness, deployment, and verifier evidence references without copying private evidence payloads.

            Non-claims:
            - no external approval claim
            - no certification claim
            - no legal-readiness claim
            - no public authority suitability claim

            Source: {InternalAudit95ProtocolTraceabilityContracts.GetString(source, "sourceId")}
            """;
    }

    public static JsonObject BuildRestrictedReviewerIndex(JsonObject source, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "feat157-restricted-reviewer-index.v1",
            ["sourceId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = InternalAudit95ProtocolTraceabilityContracts.FormatTimestamp(generatedAt),
            ["payloadInliningAllowed"] = false,
            ["rawEvidenceCopied"] = false,
            ["allowedRefTypes"] = InternalAudit95ProtocolTraceabilityContracts.Clone(
                InternalAudit95ProtocolTraceabilityContracts.RequireObject(source, "restrictedReviewerRules")["allowedRefTypes"]),
            ["restrictedRefs"] = new JsonArray(InternalAudit95ProtocolTraceabilityContracts.RequireArray(source, "sourceArtifacts")
                .OfType<JsonObject>()
                .Select(item => new JsonObject
                {
                    ["artifactId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "artifactId"),
                    ["family"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "family"),
                    ["logicalRef"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "logicalRef"),
                    ["resolvedPath"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "resolvedPath"),
                    ["expectedSha256Hash"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "expectedSha256Hash"),
                    ["visibility"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "visibility"),
                    ["classification"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "classification"),
                })
                .OrderBy(item => item["artifactId"]!.GetValue<string>(), StringComparer.Ordinal)
                .ToArray<JsonNode?>()),
        };

    public static JsonObject BuildDownstreamHandoff(JsonObject source, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "feat157-downstream-handoff.v1",
            ["sourceId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = InternalAudit95ProtocolTraceabilityContracts.FormatTimestamp(generatedAt),
            ["packageArtifactIds"] = InternalAudit95ProtocolTraceabilityContracts.ToJsonArray([
                "FEAT157-PACKAGE-MANIFEST",
                "FEAT157-AUDITOR-TRACE-MATRIX",
                "FEAT157-ARTIFACT-INVENTORY",
                "FEAT157-SCORE-PROPOSAL",
                "FEAT157-READINESS-FRAGMENT",
            ]),
            ["scopeBoundary"] = "FEAT-157 outputs may be reused as traceability inputs; they do not complete downstream dimensions.",
            ["consumers"] = InternalAudit95ProtocolTraceabilityContracts.Clone(
                InternalAudit95ProtocolTraceabilityContracts.RequireArray(source, "downstreamConsumers")),
        };
}
