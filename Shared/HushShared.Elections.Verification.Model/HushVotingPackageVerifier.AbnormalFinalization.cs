using System.Text.Json;

namespace HushShared.Elections.Verification.Model;

public sealed partial class HushVotingPackageVerifier
{
    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckAbnormalFinalizationEvidenceAsync(
        string packagePath,
        AuditPackageManifestRecord manifest,
        ElectionRecordReferenceRecord electionRecord,
        CancellationToken cancellationToken)
    {
        var abnormalClaimed = IsAbnormalFinalizationClaim(electionRecord);
        var artifactPath = ResolvePackagePath(
            packagePath,
            VerificationPackageFileNames.ReportPackageAbnormalFinalizationEvidence);
        var artifactExists = File.Exists(artifactPath);

        if (!artifactExists)
        {
            if (!abnormalClaimed)
            {
                return Array.Empty<VerifierCheckResultRecord>();
            }

            return
            [
                CreateResult(
                    AbnormalFinalizationVerificationIds.EvidenceMissingCheckCode,
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.AbnormalFinalizationEvidenceMissing,
                    "Election record declares abnormal finalization, but no abnormal-finalization evidence artifact is present.",
                    new Dictionary<string, string>
                    {
                        ["outcome_status"] = electionRecord.OutcomeStatus,
                        ["clean_finalization"] = electionRecord.CleanFinalization.ToString(),
                        ["finalization_mode"] = electionRecord.FinalizationMode,
                    }),
            ];
        }

        if (!abnormalClaimed)
        {
            return
            [
                CreateResult(
                    AbnormalFinalizationVerificationIds.EvidenceInvalidCheckCode,
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.AbnormalFinalizationClaimMismatch,
                    "An abnormal-finalization evidence artifact is present, but the election record declares clean finalization."),
            ];
        }

        var manifestEntries = manifest.Entries
            .Where(x => string.Equals(
                x.Path,
                VerificationPackageFileNames.ReportPackageAbnormalFinalizationEvidence,
                StringComparison.Ordinal))
            .ToArray();
        if (manifestEntries.Length != 1)
        {
            return
            [
                CreateResult(
                    AbnormalFinalizationVerificationIds.EvidenceInvalidCheckCode,
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.AbnormalFinalizationEvidenceInvalid,
                    "The abnormal-finalization evidence artifact must have exactly one audit manifest entry.",
                    new Dictionary<string, string>
                    {
                        ["manifest_entry_count"] = manifestEntries.Length.ToString(),
                    }),
            ];
        }

        var artifactBytes = await File.ReadAllBytesAsync(artifactPath, cancellationToken);
        var artifactHash = VerificationCanonicalHash.ComputeManifestFileSha256(artifactBytes);
        var evidence = JsonSerializer.Deserialize<AbnormalFinalizationEvidenceArtifactRecord>(
            artifactBytes,
            VerificationJson.Options);
        if (evidence is null)
        {
            return
            [
                CreateResult(
                    AbnormalFinalizationVerificationIds.EvidenceInvalidCheckCode,
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.AbnormalFinalizationEvidenceInvalid,
                    "The abnormal-finalization evidence artifact is empty or unparseable."),
            ];
        }

        var resultBinding = await ReadJsonAsync<ResultBindingArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.ResultBinding,
            cancellationToken);
        var issues = ValidateAbnormalFinalizationEvidence(
            manifest,
            electionRecord,
            resultBinding,
            evidence,
            artifactHash);
        if (issues.Count > 0)
        {
            return
            [
                CreateResult(
                    AbnormalFinalizationVerificationIds.EvidenceInvalidCheckCode,
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.AbnormalFinalizationEvidenceInvalid,
                    "The abnormal-finalization evidence artifact does not match the election record or result binding.",
                    issues.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)),
            ];
        }

        return
        [
            CreateResult(
                AbnormalFinalizationVerificationIds.EvidenceValidCheckCode,
                VerificationCheckStatus.Warn,
                VerificationResultCodes.AbnormalFinalizationEvidenceValid,
                "Abnormal finalization evidence is present and binds the copied result, authority decision, and continuity evidence. The package is evidence-complete for finalized_with_anomaly, not clean finalization.",
                new Dictionary<string, string>
                {
                    ["outcome_status"] = evidence.OutcomeStatus,
                    ["clean_finalization"] = evidence.CleanFinalization.ToString(),
                    ["finalization_mode"] = evidence.FinalizationMode,
                    ["authority_decision_ref"] = evidence.AuthorityDecisionRef,
                    ["abnormal_finalization_evidence_hash"] = artifactHash,
                }),
        ];
    }

    private static bool IsAbnormalFinalizationClaim(ElectionRecordReferenceRecord electionRecord) =>
        string.Equals(
            electionRecord.OutcomeStatus,
            AbnormalFinalizationVerificationIds.OutcomeStatusFinalizedWithAnomaly,
            StringComparison.Ordinal) ||
        !electionRecord.CleanFinalization ||
        string.Equals(
            electionRecord.FinalizationMode,
            AbnormalFinalizationVerificationIds.FinalizationModeAbnormal,
            StringComparison.Ordinal);

    private static List<KeyValuePair<string, string>> ValidateAbnormalFinalizationEvidence(
        AuditPackageManifestRecord manifest,
        ElectionRecordReferenceRecord electionRecord,
        ResultBindingArtifactRecord resultBinding,
        AbnormalFinalizationEvidenceArtifactRecord evidence,
        string artifactHash)
    {
        var issues = new List<KeyValuePair<string, string>>();

        AddIssueIfNotEqual(
            issues,
            "artifact_schema_id",
            evidence.ArtifactSchemaId,
            AbnormalFinalizationVerificationIds.ArtifactSchemaId);
        AddIssueIfNotEqual(issues, "election_id", evidence.ElectionId, electionRecord.ElectionId);
        AddIssueIfNotEqual(issues, "manifest_election_id", evidence.ElectionId, manifest.ElectionId);
        AddIssueIfNotEqual(issues, "report_package_id", evidence.ReportPackageId, resultBinding.ReportPackageId);
        AddIssueIfNotEqual(
            issues,
            "outcome_status",
            evidence.OutcomeStatus,
            AbnormalFinalizationVerificationIds.OutcomeStatusFinalizedWithAnomaly);
        AddIssueIfNotEqual(issues, "election_outcome_status", evidence.OutcomeStatus, electionRecord.OutcomeStatus);
        AddIssueIfNotEqual(issues, "binding_outcome_status", evidence.OutcomeStatus, resultBinding.OutcomeStatus);
        AddIssueIfNotEqual(
            issues,
            "finalization_mode",
            evidence.FinalizationMode,
            AbnormalFinalizationVerificationIds.FinalizationModeAbnormal);
        AddIssueIfNotEqual(issues, "election_finalization_mode", evidence.FinalizationMode, electionRecord.FinalizationMode);
        AddIssueIfNotEqual(issues, "binding_finalization_mode", evidence.FinalizationMode, resultBinding.FinalizationMode);
        AddIssueIfNotEqual(
            issues,
            "official_result_source",
            evidence.OfficialResultSource,
            AbnormalFinalizationVerificationIds.OfficialResultSourceCopiedFromFixedUnofficial);
        AddIssueIfNotEqual(issues, "unofficial_result_artifact_id", evidence.UnofficialResultArtifactId, RequiredBindingValue(resultBinding.UnofficialResultArtifactId, "unofficial_result_artifact_id"));
        AddIssueIfNotEqual(issues, "official_result_artifact_id", evidence.OfficialResultArtifactId, RequiredBindingValue(resultBinding.OfficialResultArtifactId, "official_result_artifact_id"));
        AddIssueIfNotEqual(issues, "official_result_source_artifact_id", evidence.OfficialResultSourceArtifactId, RequiredBindingValue(resultBinding.UnofficialResultArtifactId, "unofficial_result_artifact_id"));
        if (!string.IsNullOrWhiteSpace(evidence.FinalizeArtifactId) &&
            !string.IsNullOrWhiteSpace(resultBinding.FinalizeArtifactId))
        {
            AddIssueIfNotEqual(issues, "finalize_artifact_id", evidence.FinalizeArtifactId, resultBinding.FinalizeArtifactId);
        }

        if (evidence.CleanFinalization)
        {
            issues.Add(new("clean_finalization", "abnormal evidence must set clean_finalization=false"));
        }

        if (electionRecord.CleanFinalization || resultBinding.CleanFinalization)
        {
            issues.Add(new("clean_finalization_mismatch", "election record and result binding must both set clean_finalization=false"));
        }

        if (!string.IsNullOrWhiteSpace(resultBinding.AbnormalFinalizationEvidenceHash) &&
            !string.Equals(resultBinding.AbnormalFinalizationEvidenceHash, artifactHash, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new("abnormal_finalization_evidence_hash", "result binding hash does not match the evidence artifact bytes"));
        }

        RequireText(issues, "authority_decision_ref", evidence.AuthorityDecisionRef);
        RequireText(issues, "authority_decision_hash", evidence.AuthorityDecisionHash);
        RequireText(issues, "governance_rule_ref", evidence.GovernanceRuleRef);
        RequireText(issues, "close_artifact_id", evidence.CloseArtifactId);
        RequireText(issues, "public_summary", evidence.PublicSummary);
        RequireListEntriesIfPresent(issues, "missing_finalize_evidence", evidence.MissingFinalizeEvidence);
        RequireListEntriesIfPresent(issues, "continuity_incident_evidence_refs", evidence.ContinuityIncidentEvidenceRefs);
        if (!HasNonEmptyList(evidence.MissingFinalizeEvidence) &&
            !HasNonEmptyList(evidence.ContinuityIncidentEvidenceRefs))
        {
            issues.Add(new(
                "abnormal_finalization_cause",
                "at least one missing-finalize or continuity incident evidence reference is required"));
        }

        RequireList(issues, "available_trustee_acknowledgement_refs", evidence.AvailableTrusteeAcknowledgementRefs);

        if (evidence.DecidedAtUtc == default)
        {
            issues.Add(new("decided_at_utc", "decision timestamp is required"));
        }

        return issues;
    }

    private static string RequiredBindingValue(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value) ? $"missing:{fieldName}" : value;

    private static void AddIssueIfNotEqual(
        List<KeyValuePair<string, string>> issues,
        string field,
        string? actual,
        string? expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            issues.Add(new(field, $"expected '{expected ?? "null"}' but found '{actual ?? "null"}'"));
        }
    }

    private static void RequireText(
        List<KeyValuePair<string, string>> issues,
        string field,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new(field, "required"));
        }
    }

    private static void RequireList(
        List<KeyValuePair<string, string>> issues,
        string field,
        IReadOnlyList<string>? value)
    {
        if (value is not { Count: > 0 } || value.Any(string.IsNullOrWhiteSpace))
        {
            issues.Add(new(field, "at least one non-empty value is required"));
        }
    }

    private static void RequireListEntriesIfPresent(
        List<KeyValuePair<string, string>> issues,
        string field,
        IReadOnlyList<string>? value)
    {
        if (value is not null && value.Any(string.IsNullOrWhiteSpace))
        {
            issues.Add(new(field, "empty values are not allowed"));
        }
    }

    private static bool HasNonEmptyList(IReadOnlyList<string>? value) =>
        value is { Count: > 0 } && value.All(x => !string.IsNullOrWhiteSpace(x));
}
