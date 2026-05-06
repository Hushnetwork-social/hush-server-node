namespace HushShared.Elections.Model;

public static class ElectionSp05ProfileIds
{
    public const string OrganizationalEligibilityCheckoffV1 = "organizational_eligibility_checkoff_v1";
    public const string LegacyPublicNamedCheckoffDevOnly = "legacy_public_named_checkoff_dev_only";

    public const string RosterCanonicalizationV1 = "hush_roster_canonicalization_v1";
    public const string EligibilityPolicyCanonicalizationV1 = "hush_eligibility_policy_canonical_v1";
    public const string VoteCommitmentPreimageV1 = "hush_vote_commitment_preimage_v1";
    public const string VoteNullifierPreimageV1 = "hush_vote_nullifier_preimage_v1";
}

public record ElectionRosterRejectedRowRecord(
    int SourceRowNumber,
    string OrganizationVoterId,
    string ReasonCode,
    string Reason,
    IReadOnlyDictionary<string, string> RestrictedRowValues);

public record ElectionRosterDuplicateContactWarningRecord(
    ElectionRosterContactType ContactType,
    string ContactMatchKey,
    IReadOnlyList<string> OrganizationVoterIds,
    string WarningCode,
    string Warning);

public record ElectionRosterImportEvidenceRecord(
    Guid RosterImportId,
    ElectionId ElectionId,
    int RosterImportVersion,
    string RosterSourceFileHash,
    string RosterCanonicalHash,
    string RosterCanonicalizationVersion,
    string RosterCanonicalizationVersionHash,
    int AcceptedRowCount,
    int RejectedRowCount,
    int InvalidRowRejectionCount,
    int DuplicateIdRejectionCount,
    int DuplicateContactWarningCount,
    DateTime ImportedAt,
    string ImportedByActor,
    IReadOnlyList<ElectionRosterRejectedRowRecord> RejectedRows,
    IReadOnlyList<ElectionRosterDuplicateContactWarningRecord> DuplicateContactWarnings)
{
    public string RosterSourceFileHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            RosterSourceFileHash,
            nameof(RosterSourceFileHash));

    public string RosterCanonicalHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            RosterCanonicalHash,
            nameof(RosterCanonicalHash));

    public string RosterCanonicalizationVersion { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            RosterCanonicalizationVersion,
            nameof(RosterCanonicalizationVersion));

    public string RosterCanonicalizationVersionHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            RosterCanonicalizationVersionHash,
            nameof(RosterCanonicalizationVersionHash));

    public string ImportedByActor { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            ImportedByActor,
            nameof(ImportedByActor));

    public IReadOnlyList<ElectionRosterRejectedRowRecord> RejectedRows { get; init; } =
        RejectedRows?.ToArray() ?? Array.Empty<ElectionRosterRejectedRowRecord>();

    public IReadOnlyList<ElectionRosterDuplicateContactWarningRecord> DuplicateContactWarnings { get; init; } =
        DuplicateContactWarnings?.ToArray() ?? Array.Empty<ElectionRosterDuplicateContactWarningRecord>();
}

public record ElectionEligibilityPolicyEvidenceRecord(
    Guid Id,
    ElectionId ElectionId,
    string EligibilityPolicyId,
    string EligibilityPolicyVersion,
    EligibilityMutationPolicy EligibilityMutationPolicy,
    ElectionIdentityLinkPolicy IdentityLinkPolicy,
    ElectionCheckoffVisibilityPolicy CheckoffVisibilityPolicy,
    ElectionActorLinkMultiplicityPolicy ActorLinkMultiplicityPolicy,
    ElectionContactCodeProviderReadiness ContactCodeProviderReadiness,
    string EligibilityPolicyCanonicalizationVersion,
    string EligibilityPolicyCanonicalizationVersionHash,
    DateTime DeclaredAt,
    string DeclaredByActor,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId)
{
    public string EligibilityPolicyId { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            EligibilityPolicyId,
            nameof(EligibilityPolicyId));

    public string EligibilityPolicyVersion { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            EligibilityPolicyVersion,
            nameof(EligibilityPolicyVersion));

    public string EligibilityPolicyCanonicalizationVersion { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            EligibilityPolicyCanonicalizationVersion,
            nameof(EligibilityPolicyCanonicalizationVersion));

    public string EligibilityPolicyCanonicalizationVersionHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            EligibilityPolicyCanonicalizationVersionHash,
            nameof(EligibilityPolicyCanonicalizationVersionHash));

    public string DeclaredByActor { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            DeclaredByActor,
            nameof(DeclaredByActor));
}

public record ElectionCommitmentSchemeEvidenceRecord(
    Guid Id,
    ElectionId ElectionId,
    string CommitmentSchemeVersion,
    string CommitmentSchemeVersionHash,
    string NullifierSchemeVersion,
    string NullifierSchemeVersionHash,
    string RosterCanonicalizationVersion,
    string RosterCanonicalizationVersionHash,
    string EligibilityPolicyCanonicalizationVersion,
    string EligibilityPolicyCanonicalizationVersionHash,
    DateTime DeclaredAt,
    string DeclaredByActor,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId)
{
    public string CommitmentSchemeVersion { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            CommitmentSchemeVersion,
            nameof(CommitmentSchemeVersion));

    public string CommitmentSchemeVersionHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            CommitmentSchemeVersionHash,
            nameof(CommitmentSchemeVersionHash));

    public string NullifierSchemeVersion { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            NullifierSchemeVersion,
            nameof(NullifierSchemeVersion));

    public string NullifierSchemeVersionHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            NullifierSchemeVersionHash,
            nameof(NullifierSchemeVersionHash));

    public string RosterCanonicalizationVersion { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            RosterCanonicalizationVersion,
            nameof(RosterCanonicalizationVersion));

    public string RosterCanonicalizationVersionHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            RosterCanonicalizationVersionHash,
            nameof(RosterCanonicalizationVersionHash));

    public string EligibilityPolicyCanonicalizationVersion { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            EligibilityPolicyCanonicalizationVersion,
            nameof(EligibilityPolicyCanonicalizationVersion));

    public string EligibilityPolicyCanonicalizationVersionHash { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            EligibilityPolicyCanonicalizationVersionHash,
            nameof(EligibilityPolicyCanonicalizationVersionHash));

    public string DeclaredByActor { get; init; } =
        ElectionCommitmentRegistrationRecord.NormalizeRequiredValue(
            DeclaredByActor,
            nameof(DeclaredByActor));
}
