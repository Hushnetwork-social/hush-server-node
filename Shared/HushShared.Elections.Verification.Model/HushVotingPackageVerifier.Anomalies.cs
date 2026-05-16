using System.Text.Json;
using HushShared.Elections.Model;

namespace HushShared.Elections.Verification.Model;

public sealed partial class HushVotingPackageVerifier
{
    private const string RestrictedAnomalyIntakeManifestArtifactSchemaId =
        "restricted-anomaly-intake-manifest-artifact-v1";

    private const string RestrictedAnomalyIntakeManifestNodeType = "anomaly_intake_manifest";

    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckAnomalyEvidenceManifestAsync(
        string packagePath,
        AuditPackageManifestRecord manifest,
        CancellationToken cancellationToken)
    {
        var artifactPath = ResolvePackagePath(
            packagePath,
            VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest);
        var artifactExists = File.Exists(artifactPath);
        var graphNode = await TryReadAnomalyEvidenceGraphNodeAsync(packagePath, cancellationToken);

        if (!artifactExists)
        {
            if (manifest.PackageView == VerificationPackageView.RestrictedOwnerAuditor &&
                graphNode is not null)
            {
                return
                [
                    CreateResult(
                        "ANOM-001",
                        VerificationCheckStatus.Fail,
                        VerificationResultCodes.AnomalyEvidenceManifestMissing,
                        "The evidence graph references a restricted anomaly intake manifest, but the artifact is missing."),
                ];
            }

            return Array.Empty<VerifierCheckResultRecord>();
        }

        if (manifest.PackageView != VerificationPackageView.RestrictedOwnerAuditor)
        {
            return
            [
                CreateResult(
                    "ANOM-001",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PublicRestrictedFieldLeak,
                    "A restricted anomaly intake manifest is present in a public package."),
            ];
        }

        var artifactContent = await File.ReadAllTextAsync(artifactPath, cancellationToken);
        var privacyScan = ElectionAnomalyRestrictedArtifactPrivacyScanner.ScanAuditorSafeManifest(artifactContent);
        var results = new List<VerifierCheckResultRecord>();
        if (!privacyScan.Passed)
        {
            results.Add(CreateResult(
                "ANOM-005",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.AnomalyEvidenceManifestPrivacyViolation,
                "Restricted anomaly intake manifest contains auditor-unsafe identity or key material fields.",
                privacyScan.MatchedFieldNames.ToDictionary(x => x, x => "forbidden", StringComparer.Ordinal)));
        }

        var artifact = await ReadJsonAsync<RestrictedAnomalyIntakeManifestArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest,
            cancellationToken);
        var shapeIssue = ValidateAnomalyIntakeManifestArtifactShape(artifact);
        if (shapeIssue is not null)
        {
            results.Add(CreateResult(
                "ANOM-004",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.AnomalyEvidenceManifestInvalid,
                shapeIssue));
            return results;
        }

        var manifestPayload = artifact.Manifest!;
        var expectedHash = ElectionAnomalyIntakeManifestHasher.ComputeHash(manifestPayload);
        if (!string.Equals(artifact.ManifestHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(CreateResult(
                "ANOM-002",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.AnomalyEvidenceManifestHashMismatch,
                "Restricted anomaly intake manifest hash does not match the embedded manifest.",
                new Dictionary<string, string>
                {
                    ["expected"] = artifact.ManifestHash ?? string.Empty,
                    ["actual"] = expectedHash,
                }));
        }

        var summaryIssue = ValidateAnomalyIntakeManifestArtifactSummary(artifact, manifestPayload);
        if (summaryIssue is not null)
        {
            results.Add(CreateResult(
                "ANOM-004",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.AnomalyEvidenceManifestInvalid,
                summaryIssue));
        }

        if (graphNode is null)
        {
            results.Add(CreateResult(
                "ANOM-003",
                VerificationCheckStatus.Fail,
                VerificationResultCodes.AnomalyEvidenceGraphMismatch,
                "Restricted anomaly intake manifest artifact is not linked from the report evidence graph."));
        }
        else
        {
            var graphIssue = ValidateAnomalyEvidenceGraphNode(graphNode, artifact, manifestPayload);
            if (graphIssue is not null)
            {
                results.Add(CreateResult(
                    "ANOM-003",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.AnomalyEvidenceGraphMismatch,
                    graphIssue));
            }
        }

        if (results.Count == 0)
        {
            results.Add(CreateResult(
                "ANOM-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.AnomalyEvidenceManifestValid,
                "Restricted anomaly intake manifest hash, summary, and evidence graph linkage passed.",
                new Dictionary<string, string>
                {
                    ["manifest_hash"] = expectedHash,
                    ["thread_count"] = manifestPayload.Threads.Count.ToString(),
                    ["attachment_manifest_count"] = CountAnomalyManifestAttachments(manifestPayload).ToString(),
                    ["redaction_count"] = CountAnomalyManifestRedactions(manifestPayload).ToString(),
                }));
        }

        return results;
    }

    private static string? ValidateAnomalyIntakeManifestArtifactShape(
        RestrictedAnomalyIntakeManifestArtifactRecord artifact)
    {
        if (!string.Equals(
                artifact.ArtifactSchemaId,
                RestrictedAnomalyIntakeManifestArtifactSchemaId,
                StringComparison.Ordinal))
        {
            return "Restricted anomaly intake manifest artifact schema id is invalid.";
        }

        if (string.IsNullOrWhiteSpace(artifact.ManifestHash) ||
            string.IsNullOrWhiteSpace(artifact.CanonicalizationId) ||
            string.IsNullOrWhiteSpace(artifact.ScopeId) ||
            string.IsNullOrWhiteSpace(artifact.PackageReadinessStatusId) ||
            artifact.PackageReadinessBlockerIds is null ||
            artifact.Manifest is null)
        {
            return "Restricted anomaly intake manifest artifact is missing required wrapper fields.";
        }

        if (artifact.Manifest.PackageReadinessBlockerIds is null ||
            artifact.Manifest.Threads is null)
        {
            return "Restricted anomaly intake manifest is missing required collection fields.";
        }

        foreach (var thread in artifact.Manifest.Threads)
        {
            if (thread.Attachments is null ||
                thread.Redactions is null ||
                thread.RecipientStatuses is null)
            {
                return "Restricted anomaly intake manifest contains a thread with missing collection fields.";
            }
        }

        return null;
    }

    private static string? ValidateAnomalyIntakeManifestArtifactSummary(
        RestrictedAnomalyIntakeManifestArtifactRecord artifact,
        AnomalyIntakeManifest manifest)
    {
        if (!string.Equals(artifact.CanonicalizationId, manifest.CanonicalizationId, StringComparison.Ordinal) ||
            !string.Equals(artifact.ScopeId, manifest.ScopeId, StringComparison.Ordinal) ||
            !string.Equals(artifact.PackageReadinessStatusId, manifest.PackageReadinessStatusId, StringComparison.Ordinal) ||
            !StringSetsEqual(artifact.PackageReadinessBlockerIds, manifest.PackageReadinessBlockerIds))
        {
            return "Restricted anomaly intake manifest wrapper fields do not match the embedded manifest.";
        }

        if (artifact.ThreadCount != manifest.Threads.Count ||
            artifact.AttachmentManifestCount != CountAnomalyManifestAttachments(manifest) ||
            artifact.RedactionCount != CountAnomalyManifestRedactions(manifest) ||
            artifact.RecipientStatusCount != CountAnomalyManifestRecipientStatuses(manifest))
        {
            return "Restricted anomaly intake manifest wrapper counts do not match the embedded manifest.";
        }

        return null;
    }

    private static string? ValidateAnomalyEvidenceGraphNode(
        AnomalyEvidenceGraphManifestNode node,
        RestrictedAnomalyIntakeManifestArtifactRecord artifact,
        AnomalyIntakeManifest manifest)
    {
        if (!string.Equals(node.NodeType, RestrictedAnomalyIntakeManifestNodeType, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(node.ArtifactId))
        {
            return "Evidence graph restricted anomaly intake manifest node is missing its node type or artifact id.";
        }

        if (!string.Equals(node.ManifestHash, artifact.ManifestHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(node.CanonicalizationId, manifest.CanonicalizationId, StringComparison.Ordinal) ||
            !string.Equals(node.ScopeId, manifest.ScopeId, StringComparison.Ordinal) ||
            !string.Equals(node.PackageReadinessStatusId, manifest.PackageReadinessStatusId, StringComparison.Ordinal) ||
            !StringSetsEqual(node.PackageReadinessBlockerIds, manifest.PackageReadinessBlockerIds))
        {
            return "Evidence graph restricted anomaly intake manifest node does not match the artifact manifest fields.";
        }

        if (node.ThreadCount != manifest.Threads.Count ||
            node.AttachmentManifestCount != CountAnomalyManifestAttachments(manifest) ||
            node.RedactionCount != CountAnomalyManifestRedactions(manifest) ||
            node.RecipientStatusCount != CountAnomalyManifestRecipientStatuses(manifest))
        {
            return "Evidence graph restricted anomaly intake manifest node counts do not match the artifact manifest.";
        }

        if (!StringSetsEqual(node.AnomalyThreadIds, manifest.Threads.Select(x => x.AnomalyThreadId.ToString()).ToArray()) ||
            !StringSetsEqual(
                node.AttachmentManifestIds,
                manifest.Threads
                    .SelectMany(x => x.Attachments.Select(attachment => attachment.AttachmentManifestId.ToString()))
                    .ToArray()) ||
            !StringSetsEqual(
                node.RedactionEventIds,
                manifest.Threads
                    .SelectMany(x => x.Redactions.Select(redaction => redaction.RedactionEventId.ToString()))
                    .ToArray()) ||
            !StringSetsEqual(
                node.SourceEventIds,
                manifest.Threads
                    .SelectMany(x => x.Attachments.Select(attachment => attachment.EventId.ToString())
                        .Concat(x.Redactions.Select(redaction => redaction.EventId.ToString())))
                    .ToArray()))
        {
            return "Evidence graph restricted anomaly intake manifest node ids do not match the artifact manifest.";
        }

        return null;
    }

    private static async Task<AnomalyEvidenceGraphManifestNode?> TryReadAnomalyEvidenceGraphNodeAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var graphPath = ResolvePackagePath(packagePath, VerificationPackageFileNames.ReportPackageEvidenceGraph);
        if (!File.Exists(graphPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(graphPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("restrictedAnomalyIntakeManifest", out var node) ||
            node.ValueKind == JsonValueKind.Null ||
            node.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return new AnomalyEvidenceGraphManifestNode(
            GetStringProperty(node, "nodeType"),
            GetStringProperty(node, "artifactId"),
            GetStringProperty(node, "canonicalizationId"),
            GetStringProperty(node, "manifestHash"),
            GetStringProperty(node, "scopeId"),
            GetStringProperty(node, "packageReadinessStatusId"),
            GetStringArrayProperty(node, "packageReadinessBlockerIds"),
            GetIntProperty(node, "threadCount"),
            GetIntProperty(node, "attachmentManifestCount"),
            GetIntProperty(node, "redactionCount"),
            GetIntProperty(node, "recipientStatusCount"),
            GetStringArrayProperty(node, "anomalyThreadIds"),
            GetStringArrayProperty(node, "attachmentManifestIds"),
            GetStringArrayProperty(node, "redactionEventIds"),
            GetStringArrayProperty(node, "sourceEventIds"));
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.GetRawText();
    }

    private static int? GetIntProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static IReadOnlyList<string>? GetStringArrayProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return property
            .EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? string.Empty : x.GetRawText())
            .ToArray();
    }

    private static bool StringSetsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return left
            .OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(
                right.OrderBy(x => x, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static int CountAnomalyManifestAttachments(AnomalyIntakeManifest manifest) =>
        manifest.Threads.Sum(x => x.Attachments.Count);

    private static int CountAnomalyManifestRedactions(AnomalyIntakeManifest manifest) =>
        manifest.Threads.Sum(x => x.Redactions.Count);

    private static int CountAnomalyManifestRecipientStatuses(AnomalyIntakeManifest manifest) =>
        manifest.Threads.Sum(x => x.RecipientStatuses.Count);

    private sealed record AnomalyEvidenceGraphManifestNode(
        string? NodeType,
        string? ArtifactId,
        string? CanonicalizationId,
        string? ManifestHash,
        string? ScopeId,
        string? PackageReadinessStatusId,
        IReadOnlyList<string>? PackageReadinessBlockerIds,
        int? ThreadCount,
        int? AttachmentManifestCount,
        int? RedactionCount,
        int? RecipientStatusCount,
        IReadOnlyList<string>? AnomalyThreadIds,
        IReadOnlyList<string>? AttachmentManifestIds,
        IReadOnlyList<string>? RedactionEventIds,
        IReadOnlyList<string>? SourceEventIds);
}
