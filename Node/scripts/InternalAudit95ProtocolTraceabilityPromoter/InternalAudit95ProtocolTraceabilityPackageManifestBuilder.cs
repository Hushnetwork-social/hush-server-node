using System.Text.Json.Nodes;

namespace InternalAudit95ProtocolTraceabilityPromoter;

public static class InternalAudit95ProtocolTraceabilityPackageManifestBuilder
{
    public static JsonObject BuildPackageManifest(
        JsonObject source,
        string packageStatus,
        IReadOnlyList<string> blockers,
        IReadOnlyList<InternalAudit95ProtocolTraceabilityGeneratedArtifact> artifacts,
        DateTimeOffset generatedAt)
    {
        var contractsByPath = InternalAudit95ProtocolTraceabilityContracts.RequireArray(source, "generatedArtifactContracts")
            .OfType<JsonObject>()
            .ToDictionary(
                item => InternalAudit95ProtocolTraceabilityContracts.GetString(item, "fileName"),
                StringComparer.Ordinal);
        var entries = artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact =>
            {
                contractsByPath.TryGetValue(artifact.RelativePath, out var contract);
                return new JsonObject
                {
                    ["artifactId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(contract, "artifactId", artifact.RelativePath),
                    ["fileName"] = artifact.RelativePath,
                    ["sha256Hash"] = $"sha256:{artifact.Sha256Hash}",
                    ["mediaType"] = artifact.MediaType,
                    ["visibility"] = InternalAudit95ProtocolTraceabilityContracts.GetString(contract, "visibility", "internal"),
                    ["classification"] = InternalAudit95ProtocolTraceabilityContracts.GetString(contract, "classification", "supporting"),
                };
            })
            .ToArray();
        var manifestHashInput = new JsonObject
        {
            ["entries"] = new JsonArray(entries.Select(entry => (JsonNode?)entry.DeepClone()).ToArray()),
        };

        return new JsonObject
        {
            ["schemaVersion"] = "feat157-package-manifest.v1",
            ["packageId"] = InternalAudit95ProtocolTraceabilityContracts.PackageAnchor,
            ["sourceId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = InternalAudit95ProtocolTraceabilityContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["sha256Hash"] = $"sha256:{InternalAudit95ProtocolTraceabilityContracts.Sha256Hex(InternalAudit95ProtocolTraceabilityContracts.CanonicalJson(manifestHashInput))}",
            ["directRegisterMutation"] = false,
            ["blockers"] = InternalAudit95ProtocolTraceabilityContracts.ToJsonArray(blockers),
            ["entries"] = new JsonArray(entries.Select(entry => (JsonNode?)entry.DeepClone()).ToArray()),
        };
    }
}
