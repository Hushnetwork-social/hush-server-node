namespace HushShared.Elections.Model;

public static partial class ElectionModelFactory
{
    public static ElectionRosterImportEvidenceRecord CreateRosterImportEvidence(
        ElectionId electionId,
        int rosterImportVersion,
        string rosterSourceFileHash,
        string rosterCanonicalHash,
        string rosterCanonicalizationVersion,
        string rosterCanonicalizationVersionHash,
        int acceptedRowCount,
        int rejectedRowCount,
        int invalidRowRejectionCount,
        int duplicateIdRejectionCount,
        int duplicateContactWarningCount,
        string importedByActor,
        IReadOnlyList<ElectionRosterRejectedRowRecord>? rejectedRows = null,
        IReadOnlyList<ElectionRosterDuplicateContactWarningRecord>? duplicateContactWarnings = null,
        DateTime? importedAt = null)
    {
        EnsureNonNegative(acceptedRowCount, nameof(acceptedRowCount));
        EnsureNonNegative(rejectedRowCount, nameof(rejectedRowCount));
        EnsureNonNegative(invalidRowRejectionCount, nameof(invalidRowRejectionCount));
        EnsureNonNegative(duplicateIdRejectionCount, nameof(duplicateIdRejectionCount));
        EnsureNonNegative(duplicateContactWarningCount, nameof(duplicateContactWarningCount));

        if (rosterImportVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rosterImportVersion), "Roster import version must be at least 1.");
        }

        return new ElectionRosterImportEvidenceRecord(
            Guid.NewGuid(),
            electionId,
            rosterImportVersion,
            NormalizeRequiredText(rosterSourceFileHash, nameof(rosterSourceFileHash)),
            NormalizeRequiredText(rosterCanonicalHash, nameof(rosterCanonicalHash)),
            NormalizeRequiredText(rosterCanonicalizationVersion, nameof(rosterCanonicalizationVersion)),
            NormalizeRequiredText(rosterCanonicalizationVersionHash, nameof(rosterCanonicalizationVersionHash)),
            acceptedRowCount,
            rejectedRowCount,
            invalidRowRejectionCount,
            duplicateIdRejectionCount,
            duplicateContactWarningCount,
            importedAt ?? DateTime.UtcNow,
            NormalizeRequiredText(importedByActor, nameof(importedByActor)),
            rejectedRows ?? Array.Empty<ElectionRosterRejectedRowRecord>(),
            duplicateContactWarnings ?? Array.Empty<ElectionRosterDuplicateContactWarningRecord>());
    }

    public static ElectionEligibilityPolicyEvidenceRecord CreateEligibilityPolicyEvidence(
        ElectionId electionId,
        string eligibilityPolicyVersion,
        EligibilityMutationPolicy eligibilityMutationPolicy,
        ElectionIdentityLinkPolicy identityLinkPolicy,
        ElectionCheckoffVisibilityPolicy checkoffVisibilityPolicy,
        ElectionActorLinkMultiplicityPolicy actorLinkMultiplicityPolicy,
        ElectionContactCodeProviderReadiness contactCodeProviderReadiness,
        string eligibilityPolicyCanonicalizationVersionHash,
        string declaredByActor,
        string eligibilityPolicyId = ElectionSp05ProfileIds.OrganizationalEligibilityCheckoffV1,
        string eligibilityPolicyCanonicalizationVersion = ElectionSp05ProfileIds.EligibilityPolicyCanonicalizationV1,
        DateTime? declaredAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null) =>
        new(
            Guid.NewGuid(),
            electionId,
            NormalizeRequiredText(eligibilityPolicyId, nameof(eligibilityPolicyId)),
            NormalizeRequiredText(eligibilityPolicyVersion, nameof(eligibilityPolicyVersion)),
            eligibilityMutationPolicy,
            identityLinkPolicy,
            checkoffVisibilityPolicy,
            actorLinkMultiplicityPolicy,
            contactCodeProviderReadiness,
            NormalizeRequiredText(eligibilityPolicyCanonicalizationVersion, nameof(eligibilityPolicyCanonicalizationVersion)),
            NormalizeRequiredText(eligibilityPolicyCanonicalizationVersionHash, nameof(eligibilityPolicyCanonicalizationVersionHash)),
            declaredAt ?? DateTime.UtcNow,
            NormalizeRequiredText(declaredByActor, nameof(declaredByActor)),
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);

    public static ElectionCommitmentSchemeEvidenceRecord CreateCommitmentSchemeEvidence(
        ElectionId electionId,
        string commitmentSchemeVersionHash,
        string nullifierSchemeVersionHash,
        string rosterCanonicalizationVersionHash,
        string eligibilityPolicyCanonicalizationVersionHash,
        string declaredByActor,
        string commitmentSchemeVersion = ElectionSp05ProfileIds.VoteCommitmentPreimageV1,
        string nullifierSchemeVersion = ElectionSp05ProfileIds.VoteNullifierPreimageV1,
        string rosterCanonicalizationVersion = ElectionSp05ProfileIds.RosterCanonicalizationV1,
        string eligibilityPolicyCanonicalizationVersion = ElectionSp05ProfileIds.EligibilityPolicyCanonicalizationV1,
        DateTime? declaredAt = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null) =>
        new(
            Guid.NewGuid(),
            electionId,
            NormalizeRequiredText(commitmentSchemeVersion, nameof(commitmentSchemeVersion)),
            NormalizeRequiredText(commitmentSchemeVersionHash, nameof(commitmentSchemeVersionHash)),
            NormalizeRequiredText(nullifierSchemeVersion, nameof(nullifierSchemeVersion)),
            NormalizeRequiredText(nullifierSchemeVersionHash, nameof(nullifierSchemeVersionHash)),
            NormalizeRequiredText(rosterCanonicalizationVersion, nameof(rosterCanonicalizationVersion)),
            NormalizeRequiredText(rosterCanonicalizationVersionHash, nameof(rosterCanonicalizationVersionHash)),
            NormalizeRequiredText(eligibilityPolicyCanonicalizationVersion, nameof(eligibilityPolicyCanonicalizationVersion)),
            NormalizeRequiredText(eligibilityPolicyCanonicalizationVersionHash, nameof(eligibilityPolicyCanonicalizationVersionHash)),
            declaredAt ?? DateTime.UtcNow,
            NormalizeRequiredText(declaredByActor, nameof(declaredByActor)),
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);

    private static void EnsureNonNegative(int value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "Value cannot be negative.");
        }
    }
}
