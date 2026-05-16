namespace HushShared.Elections.Model;

public static class ElectionAnomalyRetentionEvidenceStatusIds
{
    public const string NoAnomalyHoldEvidence = "no_anomaly_hold_evidence";
    public const string OpenCaseRequiresPolicyReview = "open_case_requires_policy_review";
    public const string RestrictedRedactionHoldReferencePresent = "restricted_redaction_hold_reference_present";
    public const string GovernedHoldReferenceRecorded = "governed_hold_reference_recorded";
    public const string RetentionHoldNotImplemented = "retention_hold_not_implemented";
}

public static class ElectionAnomalyReportGenerationReadOnlyStatusIds
{
    public const string Validated = "validated";
    public const string NotValidated = "not_validated";
}

public sealed record AnomalyRetentionEvidenceStatus(
    string StatusId,
    IReadOnlyList<string> GovernedDecisionRefs,
    int RedactionHoldReferenceCount,
    int OpenCaseCount,
    int EscalatedCaseCount,
    bool ReadinessBlocksValidationClaims,
    string Message);

public sealed record AnomalyReportReadinessProjection(
    string PublicSummarySchemaId,
    string SuppressionPolicyId,
    string ForbiddenFieldScanStatusId,
    Guid? RestrictedManifestArtifactId,
    string? RestrictedManifestHash,
    string PackageReadinessStatusId,
    IReadOnlyList<string> PackageReadinessBlockerIds,
    int OpenCaseCount,
    int EscalatedCaseCount,
    string RetentionEvidenceStatusId,
    AnomalyRetentionEvidenceStatus RetentionEvidenceStatus,
    bool HasGovernedLifecycleEvidence,
    string ReportGenerationReadOnlyStatusId);

public sealed record AnomalyReportReadinessProjectionBuildRequest(
    PublicAnomalySummary PublicSummary,
    AnomalyIntakeManifest? RestrictedAnomalyIntakeManifest,
    string ForbiddenFieldScanStatusId,
    bool ReportGenerationReadOnlyValidated = true);

public static class ElectionAnomalyReportReadinessProjectionBuilder
{
    public static AnomalyReportReadinessProjection Build(AnomalyReportReadinessProjectionBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.PublicSummary);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ForbiddenFieldScanStatusId);

        var manifest = request.RestrictedAnomalyIntakeManifest;
        var retention = BuildRetentionEvidenceStatus(manifest);
        var governedDecisionRefs = retention.GovernedDecisionRefs;

        return new AnomalyReportReadinessProjection(
            request.PublicSummary.SchemaId,
            request.PublicSummary.SuppressionPolicyId,
            request.ForbiddenFieldScanStatusId,
            request.PublicSummary.RestrictedManifestArtifactId,
            request.PublicSummary.RestrictedManifestHash,
            manifest?.PackageReadinessStatusId ?? ElectionAnomalyPackageReadinessStatusIds.Ready,
            manifest?.PackageReadinessBlockerIds
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>(),
            retention.OpenCaseCount,
            retention.EscalatedCaseCount,
            retention.StatusId,
            retention,
            governedDecisionRefs.Count > 0,
            request.ReportGenerationReadOnlyValidated
                ? ElectionAnomalyReportGenerationReadOnlyStatusIds.Validated
                : ElectionAnomalyReportGenerationReadOnlyStatusIds.NotValidated);
    }

    public static AnomalyRetentionEvidenceStatus BuildRetentionEvidenceStatus(AnomalyIntakeManifest? manifest)
    {
        var threads = manifest?.Threads ?? Array.Empty<AnomalyIntakeManifestThread>();
        if (threads.Count == 0)
        {
            return new AnomalyRetentionEvidenceStatus(
                ElectionAnomalyRetentionEvidenceStatusIds.NoAnomalyHoldEvidence,
                Array.Empty<string>(),
                RedactionHoldReferenceCount: 0,
                OpenCaseCount: 0,
                EscalatedCaseCount: 0,
                ReadinessBlocksValidationClaims: false,
                "No anomaly hold evidence is present because no anomaly threads are included in the package manifest.");
        }

        var openCaseCount = threads.Count(thread => !ElectionAnomalyCaseStateIds.IsTerminal(thread.CaseStateId));
        var escalatedCaseCount = threads.Count(thread =>
            string.Equals(thread.CaseStateId, ElectionAnomalyCaseStateIds.EscalatedToGovernedDecision, StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(thread.GovernedDecisionRef));
        var governedRefs = threads
            .Select(x => x.GovernedDecisionRef)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var redactionHoldReferenceCount = threads
            .SelectMany(x => x.Redactions)
            .Count(redaction => string.Equals(
                redaction.ReasonCodeId,
                ElectionAnomalyRedactionReasonIds.LegalHold,
                StringComparison.Ordinal));

        if (openCaseCount > 0)
        {
            return new AnomalyRetentionEvidenceStatus(
                ElectionAnomalyRetentionEvidenceStatusIds.OpenCaseRequiresPolicyReview,
                governedRefs,
                redactionHoldReferenceCount,
                openCaseCount,
                escalatedCaseCount,
                ReadinessBlocksValidationClaims: true,
                "Open anomaly cases require policy review before readiness claims treat anomaly handling as complete.");
        }

        if (governedRefs.Length > 0)
        {
            return new AnomalyRetentionEvidenceStatus(
                ElectionAnomalyRetentionEvidenceStatusIds.GovernedHoldReferenceRecorded,
                governedRefs,
                redactionHoldReferenceCount,
                openCaseCount,
                escalatedCaseCount,
                ReadinessBlocksValidationClaims: false,
                "Governed anomaly lifecycle evidence is recorded by reference; FEAT-129 did not activate the hold.");
        }

        if (redactionHoldReferenceCount > 0)
        {
            return new AnomalyRetentionEvidenceStatus(
                ElectionAnomalyRetentionEvidenceStatusIds.RestrictedRedactionHoldReferencePresent,
                governedRefs,
                redactionHoldReferenceCount,
                openCaseCount,
                escalatedCaseCount,
                ReadinessBlocksValidationClaims: true,
                "Restricted redaction hold references are present and require retention-policy review.");
        }

        return new AnomalyRetentionEvidenceStatus(
            ElectionAnomalyRetentionEvidenceStatusIds.RetentionHoldNotImplemented,
            governedRefs,
            redactionHoldReferenceCount,
            openCaseCount,
            escalatedCaseCount,
            ReadinessBlocksValidationClaims: true,
            "Anomaly evidence exists, but no governed retention hold evidence is recorded in this FEAT-129 report projection.");
    }
}
