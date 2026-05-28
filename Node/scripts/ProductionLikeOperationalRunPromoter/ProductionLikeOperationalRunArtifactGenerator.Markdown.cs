using System.Text;
using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static partial class ProductionLikeOperationalRunArtifactGenerator
{
    private static string BuildPublicSafeSummary(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        string packageStatus,
        DateTimeOffset generatedAt)
    {
        var profile = ProductionLikeOperationalRunContracts.RequireObject(source, "runProfile");
        var register = ProductionLikeOperationalRunContracts.RequireObject(source, "baselineRegister");
        var scoreAllowed = IsScoreMovementAllowed(gate, packageStatus);
        var builder = new StringBuilder();

        builder.AppendLine("# Production-Like Operational Run Summary");
        builder.AppendLine();
        builder.AppendLine($"Generated: {ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt)}");
        builder.AppendLine($"Source: {ProductionLikeOperationalRunContracts.GetString(source, "sourceId")}");
        builder.AppendLine($"Status: {packageStatus}");
        builder.AppendLine($"Profile: {ProductionLikeOperationalRunContracts.GetString(profile, "profileId")}");
        builder.AppendLine();
        builder.AppendLine("## Reviewer Summary");
        builder.AppendLine("- Evidence type: controlled production-like HushVoting operational run package.");
        builder.Append("- Scope: ");
        builder.AppendLine(ProductionLikeOperationalRunContracts.GetString(profile, "scope"));
        builder.AppendLine("- Register effect: score proposal input only; no direct readiness-register mutation.");
        builder.AppendLine("- Reviewer use: FEAT-156 may consume this package when promotion evidence is evaluated.");
        builder.AppendLine();
        builder.AppendLine(scoreAllowed
            ? $"RDY-DIM-007 can be proposed from {ProductionLikeOperationalRunContracts.GetInt(register, "currentScore")} to {ProductionLikeOperationalRunContracts.GetInt(register, "targetScore")} through FEAT-156 review."
            : $"No RDY-DIM-007 score movement is proposed while this package is {packageStatus}.");
        builder.AppendLine();
        builder.AppendLine("## Non-Claims");
        builder.AppendLine("- This package does not claim production rollout readiness.");
        builder.AppendLine("- This package does not claim public/state election readiness.");
        builder.AppendLine("- This package does not claim legal sufficiency.");
        builder.AppendLine("- This package does not claim certification or external validation.");
        builder.AppendLine("- This package does not claim failed-finalize continuity completion; FEAT-155 owns that proof.");
        builder.AppendLine("- This package does not prove repeated operating history or customer-site equivalence.");
        builder.AppendLine();
        builder.AppendLine("## Diagnostics");
        foreach (var blocker in gate.Blockers)
        {
            builder.Append("- ");
            builder.AppendLine(blocker);
        }

        if (gate.Blockers.Count == 0)
        {
            builder.AppendLine("- none");
        }

        return builder.ToString();
    }

    private static string BuildRestrictedEvidenceIndex(JsonObject source, DateTimeOffset generatedAt)
    {
        var refs = CollectRestrictedRefs(source);
        var builder = new StringBuilder();
        builder.AppendLine("# Restricted Reviewer Index");
        builder.AppendLine();
        builder.AppendLine($"Generated: {ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt)}");
        builder.AppendLine($"Source: {ProductionLikeOperationalRunContracts.GetString(source, "sourceId")}");
        builder.AppendLine();
        builder.AppendLine("Payload policy: this restricted reviewer index stores references and hashes only. Payload bodies are not copied.");
        builder.AppendLine("Reviewer use: resolve path refs in the restricted evidence store and compare hashes before review.");
        builder.AppendLine();
        builder.AppendLine("| Ref | Visibility | Path Ref | Hash | Payload Copied |");
        builder.AppendLine("|-----|------------|----------|------|----------------|");

        foreach (var reference in refs)
        {
            builder.Append("| ");
            builder.Append(ProductionLikeOperationalRunContracts.GetString(reference, "id"));
            builder.Append(" | ");
            builder.Append(ProductionLikeOperationalRunContracts.GetString(reference, "visibility"));
            builder.Append(" | ");
            builder.Append(ProductionLikeOperationalRunContracts.GetString(reference, "pathRef"));
            builder.Append(" | ");
            builder.Append(ProductionLikeOperationalRunContracts.GetString(reference, "sha256Hash"));
            builder.Append(" | false |");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildReadme(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        string packageStatus,
        DateTimeOffset generatedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# FEAT-154 Production-Like Operational Run Package");
        builder.AppendLine();
        builder.AppendLine($"Generated: {ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt)}");
        builder.AppendLine($"Status: {packageStatus}");
        builder.AppendLine();
        builder.AppendLine("This package records a controlled production-like HushVoting operational run for RDY-DIM-007.");
        builder.AppendLine("It is read-only evidence for later FEAT-156 promotion review and does not mutate the readiness register.");
        builder.AppendLine();
        builder.AppendLine("## Key Files");
        foreach (var path in RequiredArtifactPaths)
        {
            builder.Append("- ");
            builder.AppendLine(path);
        }

        builder.AppendLine();
        builder.AppendLine("## Boundaries");
        builder.AppendLine("- Public-safe markdown contains only bounded summaries.");
        builder.AppendLine("- Restricted evidence is referenced by identifier, path, and hash.");
        builder.AppendLine("- This package does not claim production rollout readiness.");
        builder.AppendLine("- This package does not claim public/state election readiness.");
        builder.AppendLine("- This package does not claim legal sufficiency, certification, or external validation.");
        builder.AppendLine("- This package does not claim failed-finalize continuity completion.");
        builder.AppendLine();
        builder.AppendLine("## Gate Result");
        builder.AppendLine($"Gate status: {gate.Status}");
        builder.AppendLine($"Score movement allowed: {IsScoreMovementAllowed(gate, packageStatus).ToString().ToLowerInvariant()}");
        builder.AppendLine($"Source: {ProductionLikeOperationalRunContracts.GetString(source, "sourceId")}");

        return builder.ToString();
    }

    private static IReadOnlyList<JsonObject> CollectRestrictedRefs(JsonObject source)
    {
        var refs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        AddRefsFromArray(source, "restrictedEvidenceRefs");
        AddNestedRefs(source);
        return refs.Values
            .OrderBy(item => ProductionLikeOperationalRunContracts.GetString(item, "id"), StringComparer.Ordinal)
            .ToArray();

        void AddNestedRefs(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (LooksLikeEvidenceRef(obj))
                {
                    AddRef(obj);
                }

                foreach (var child in obj.Select(property => property.Value))
                {
                    AddNestedRefs(child);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var child in array)
                {
                    AddNestedRefs(child);
                }
            }
        }

        void AddRefsFromArray(JsonObject container, string property)
        {
            if (container.TryGetPropertyValue(property, out var node) && node is JsonArray array)
            {
                foreach (var item in array.OfType<JsonObject>())
                {
                    AddRef(item);
                }
            }
        }

        void AddRef(JsonObject reference)
        {
            var id = ProductionLikeOperationalRunContracts.GetString(reference, "evidenceId",
                ProductionLikeOperationalRunContracts.GetString(reference, "refId"));
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            refs.TryAdd(id, new JsonObject
            {
                ["id"] = id,
                ["visibility"] = ProductionLikeOperationalRunContracts.GetString(reference, "visibility", "restricted"),
                ["pathRef"] = ProductionLikeOperationalRunContracts.GetString(reference, "restrictedRef",
                    ProductionLikeOperationalRunContracts.GetString(reference, "pathRef")),
                ["sha256Hash"] = ProductionLikeOperationalRunContracts.GetString(reference, "sha256Hash"),
            });
        }
    }

    private static bool LooksLikeEvidenceRef(JsonObject reference) =>
        (reference.ContainsKey("evidenceId") || reference.ContainsKey("refId")) &&
        (reference.ContainsKey("restrictedRef") || reference.ContainsKey("pathRef") || reference.ContainsKey("sha256Hash"));
}
