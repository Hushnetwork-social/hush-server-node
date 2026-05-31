using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FailedFinalizeContinuityRehearsalPromoter;

public sealed record FailedFinalizeContinuityReviewerOutputs(
    string PublicSafeSummary,
    string RestrictedEvidenceIndexJson,
    FailedFinalizePublicSafetyScanResult PublicSafetyScan,
    FailedFinalizeNoUiBoundaryEvaluation NoUiBoundary);

public sealed record FailedFinalizePublicSafetyScanResult(
    bool Passed,
    IReadOnlyList<string> Findings);

public sealed record FailedFinalizeNoUiBoundaryEvaluation(
    string Status,
    bool HasUiChanges,
    IReadOnlyList<string> ChangedUiFiles,
    string Note);

public sealed record FailedFinalizeRestrictedEvidenceIndexRecord(
    string SchemaVersion,
    string SourceId,
    string Visibility,
    IReadOnlyList<FailedFinalizeRestrictedEvidenceIndexEntryRecord> Entries,
    string PublicBoundary);

public sealed record FailedFinalizeRestrictedEvidenceIndexEntryRecord(
    string EvidenceId,
    string Path,
    string Purpose,
    string Visibility,
    string Sha256Hash,
    string PublicReference);

public static class FailedFinalizeContinuityReviewerOutputGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly string[] ForbiddenPublicMaterialNeedles =
    [
        "private key",
        "vote choice",
        "voter address",
        "trustee secret",
        "trustee share",
        "local path",
        "support transcript",
        "file://",
        "localhost",
        "c:\\",
    ];

    public static FailedFinalizeContinuityReviewerOutputs Generate(
        JsonObject source,
        IEnumerable<string>? changedFiles = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var summary = RenderPublicSafeSummary(source);
        var restrictedIndexJson = RenderRestrictedEvidenceIndex(source);
        var publicSafetyScan = ScanPublicText(summary);
        var noUiBoundary = EvaluateNoUiBoundary(changedFiles ?? []);

        return new FailedFinalizeContinuityReviewerOutputs(
            summary,
            restrictedIndexJson,
            publicSafetyScan,
            noUiBoundary);
    }

    public static FailedFinalizePublicSafetyScanResult ScanPublicText(string publicText)
    {
        var findings = ForbiddenPublicMaterialNeedles
            .Where(needle => publicText.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Select(needle => $"Public output contains forbidden material marker '{needle}'.")
            .OrderBy(finding => finding, StringComparer.Ordinal)
            .ToArray();

        return new FailedFinalizePublicSafetyScanResult(findings.Length == 0, findings);
    }

    public static FailedFinalizeNoUiBoundaryEvaluation EvaluateNoUiBoundary(IEnumerable<string> changedFiles)
    {
        var uiFiles = changedFiles
            .Select(path => path.Replace('\\', '/'))
            .Where(path => path.StartsWith("hush-web-client/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/hush-web-client/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return uiFiles.Length == 0
            ? new FailedFinalizeNoUiBoundaryEvaluation(
                "confirmed",
                HasUiChanges: false,
                ChangedUiFiles: [],
                "FEAT-155 v1 remains backend/package evidence work; no HushWebClient route, menu, or owner action UI was added.")
            : new FailedFinalizeNoUiBoundaryEvaluation(
                "blocked",
                HasUiChanges: true,
                ChangedUiFiles: uiFiles,
                "HushWebClient files require an accepted UI design before FEAT-155 can claim the no-UI boundary.");
    }

    private static string RenderPublicSafeSummary(JsonObject source)
    {
        var sourceId = FailedFinalizeContinuityContracts.GetString(source, "sourceId");
        var governedOutcome = FailedFinalizeContinuityContracts.TryObject(source, "governedOutcome");
        var noCleanResult = FailedFinalizeContinuityContracts.TryObject(source, "noCleanResult");
        var publicStatus = FailedFinalizeContinuityContracts.TryObject(source, "publicSafeStatus");
        var downstreamHandoff = FailedFinalizeContinuityContracts.TryObject(source, "downstreamHandoff");
        var residualRisks = FailedFinalizeContinuityContracts.GetStringArray(source, "residualRisks");
        var riskLines = residualRisks.Count == 0
            ? "- No residual risks were supplied."
            : string.Join('\n', residualRisks.Select(risk => $"- {risk}"));

        return NormalizeMarkdown($"""
            # Failed-Finalize Continuity Public Summary

            Source: {sourceId}
            Outcome: {FailedFinalizeContinuityContracts.GetString(governedOutcome, "outcomeStatus")}
            Package status: {FailedFinalizeContinuityContracts.GetString(publicStatus, "packageStatus")}
            Verifier result: {FailedFinalizeContinuityContracts.GetString(noCleanResult, "verifierResultCode")}

            Result boundary:
            - No valid official result exists for this package.
            - Clean finalization is not claimed.
            - No clean final package or finalize boundary is claimed.
            - Optional FEAT-138 void continuation remains the terminal void-status path.

            Public evidence:
            - Public status hash: {FailedFinalizeContinuityContracts.GetString(publicStatus, "publicHash")}
            - Source package hash: {FailedFinalizeContinuityContracts.GetString(downstreamHandoff, "sourcePackageHash")}

            Claim boundaries:
            - Legal remedy sufficiency is not claimed.
            - Production organizational rollout readiness is not claimed.
            - Public/state election readiness is not claimed.
            - Restricted trustee, voter, ballot, support, and legal payloads are not included in this public summary.

            Residual risks:
            {riskLines}
            """);
    }

    private static string RenderRestrictedEvidenceIndex(JsonObject source)
    {
        var sourceId = FailedFinalizeContinuityContracts.GetString(source, "sourceId");
        var refs = FailedFinalizeContinuityContracts.TryArray(source, "restrictedEvidenceRefs") ?? new JsonArray();
        var entries = refs
            .OfType<JsonObject>()
            .Select(reference =>
            {
                var evidenceId = FailedFinalizeContinuityContracts.GetString(reference, "evidenceId");
                var hash = FailedFinalizeContinuityContracts.GetString(reference, "sha256Hash");
                return new FailedFinalizeRestrictedEvidenceIndexEntryRecord(
                    evidenceId,
                    FailedFinalizeContinuityContracts.GetString(reference, "path"),
                    Purpose: "Failed-finalize continuity reviewer evidence reference.",
                    Visibility: "restricted_owner_auditor",
                    Sha256Hash: hash,
                    PublicReference: string.IsNullOrWhiteSpace(hash) ? evidenceId : $"sha256:{hash}");
            })
            .OrderBy(entry => entry.EvidenceId, StringComparer.Ordinal)
            .ToArray();

        var index = new FailedFinalizeRestrictedEvidenceIndexRecord(
            SchemaVersion: "failed-finalize-restricted-evidence-index.v1",
            sourceId,
            Visibility: "restricted_owner_auditor",
            entries,
            PublicBoundary: "Public artifacts may reference restricted evidence ids and hashes only; restricted payloads stay outside public output.");

        return JsonSerializer.Serialize(index, JsonOptions);
    }

    private static string NormalizeMarkdown(string value)
    {
        var lines = value.Replace("\r\n", "\n").Split('\n');
        var normalized = new StringBuilder();
        foreach (var line in lines)
        {
            normalized.AppendLine(line.TrimEnd());
        }

        return normalized.ToString().Trim() + Environment.NewLine;
    }
}
