using HushShared.Elections.Model;

namespace HushShared.Elections.Verification.Model;

public record ElectionSp05PolicyArtifactRecord(
    string ElectionId,
    string EligibilityPolicyId,
    string EligibilityPolicyVersion,
    EligibilityMutationPolicy EligibilityMutationPolicy,
    ElectionIdentityLinkPolicy IdentityLinkPolicy,
    ElectionCheckoffVisibilityPolicy CheckoffVisibilityPolicy,
    ElectionActorLinkMultiplicityPolicy ActorLinkMultiplicityPolicy,
    ElectionContactCodeProviderReadiness ContactCodeProviderReadiness,
    string RosterCanonicalizationVersion,
    string RosterCanonicalizationVersionHash,
    string EligibilityPolicyCanonicalizationVersion,
    string EligibilityPolicyCanonicalizationVersionHash,
    string CommitmentSchemeVersion,
    string CommitmentSchemeVersionHash,
    string NullifierSchemeVersion,
    string NullifierSchemeVersionHash);

public record ElectionSp05SummaryArtifactRecord(
    string ElectionId,
    string RosterSourceFileHash,
    string RosterCanonicalHash,
    string RosterOpenHash,
    string ActiveDenominatorOpenHash,
    string ActiveDenominatorCloseHash,
    string CommitmentTreeRoot,
    int RosteredCount,
    int LinkedCount,
    int ActiveDenominatorCount,
    int CommitmentCount,
    int CountedParticipationCount,
    int BlankCount,
    int DidNotVoteCount,
    int LateActivationCount,
    int BlockedActivationCount,
    IReadOnlyList<string> PublicPrivacyBoundary);

public record ElectionSp05VerifierOutputArtifactRecord(
    string ElectionId,
    string VerifierProfileId,
    DateTime VerifiedAt,
    IReadOnlyList<VerifierCheckResultRecord> Results);

public record ElectionSp05RestrictedRosterImportEvidenceArtifactRecord(
    string ElectionId,
    ElectionRosterImportEvidenceRecord ImportEvidence);

public record ElectionSp05RestrictedRosterArtifactRecord(
    string ElectionId,
    IReadOnlyList<ElectionSp05RestrictedRosterEntryArtifactRecord> Entries);

public record ElectionSp05RestrictedRosterEntryArtifactRecord(
    string ElectionId,
    string OrganizationVoterId,
    ElectionRosterContactType ContactType,
    string ContactValue,
    string? DisplayLabel,
    string? VoterGroup,
    string? VotingWeightRef,
    string? LegalBasisRef,
    ElectionVoterLinkStatus LinkStatus,
    string? LinkedActorPublicAddress,
    ElectionVotingRightStatus VotingRightStatus);

public record ElectionSp05RestrictedLinkingEvidenceArtifactRecord(
    string ElectionId,
    IReadOnlyList<ElectionSp05RestrictedLinkEvidenceRecord> Links);

public record ElectionSp05RestrictedLinkEvidenceRecord(
    string OrganizationVoterId,
    string? LinkedActorPublicAddress,
    ElectionIdentityLinkPolicy IdentityLinkPolicy,
    string IdentityEvidenceHash,
    DateTime? LinkedAt,
    string LinkStatus);

public record ElectionSp05RestrictedActivationEventsArtifactRecord(
    string ElectionId,
    IReadOnlyList<ElectionEligibilityActivationEventRecord> ActivationEvents);

public record ElectionSp05RestrictedCheckoffLedgerArtifactRecord(
    string ElectionId,
    IReadOnlyList<ElectionSp05RestrictedCheckoffLedgerEntryRecord> Entries);

public record ElectionSp05RestrictedCheckoffLedgerEntryRecord(
    string ElectionId,
    string OrganizationVoterId,
    ElectionParticipationStatus ParticipationStatus,
    Guid? CheckoffConsumptionId,
    DateTime? ConsumedAt,
    string? AcceptedBallotReference);

public record ElectionSp05RestrictedDisputesArtifactRecord(
    string ElectionId,
    IReadOnlyList<string> Disputes);
