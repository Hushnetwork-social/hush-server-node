using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections.Storage;

public interface IElectionsRepository : IRepository
{
    Task<ElectionRecord?> GetElectionAsync(ElectionId electionId);

    Task<ElectionRecord?> GetElectionForUpdateAsync(ElectionId electionId);

    Task<IReadOnlyList<ElectionRecord>> GetElectionsByOwnerAsync(string ownerPublicAddress);

    Task<IReadOnlyList<ElectionRecord>> GetElectionsByIdsAsync(IReadOnlyCollection<ElectionId> electionIds) =>
        Task.FromResult<IReadOnlyList<ElectionRecord>>(Array.Empty<ElectionRecord>());

    Task<IReadOnlyList<ElectionRecord>> SearchElectionsAsync(
        string? searchTerm,
        IReadOnlyCollection<string>? ownerPublicAddresses,
        int limit = 20) =>
        Task.FromResult<IReadOnlyList<ElectionRecord>>(Array.Empty<ElectionRecord>());

    Task SaveElectionAsync(ElectionRecord election);

    Task<IReadOnlyList<ElectionDraftSnapshotRecord>> GetDraftSnapshotsAsync(ElectionId electionId);

    Task<ElectionDraftSnapshotRecord?> GetLatestDraftSnapshotAsync(ElectionId electionId);

    Task SaveDraftSnapshotAsync(ElectionDraftSnapshotRecord snapshot);

    Task<ElectionEnvelopeAccessRecord?> GetElectionEnvelopeAccessAsync(ElectionId electionId, string actorPublicAddress);

    Task SaveElectionEnvelopeAccessAsync(ElectionEnvelopeAccessRecord accessRecord);

    Task UpdateElectionEnvelopeAccessAsync(ElectionEnvelopeAccessRecord accessRecord);

    Task DeleteElectionEnvelopeAccessAsync(ElectionId electionId, string actorPublicAddress);

    Task<IReadOnlyList<ElectionResultArtifactRecord>> GetResultArtifactsAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionResultArtifactRecord>>(Array.Empty<ElectionResultArtifactRecord>());

    Task<ElectionResultArtifactRecord?> GetResultArtifactAsync(Guid resultArtifactId) =>
        Task.FromResult<ElectionResultArtifactRecord?>(null);

    Task<ElectionResultArtifactRecord?> GetResultArtifactAsync(ElectionId electionId, ElectionResultArtifactKind artifactKind) =>
        Task.FromResult<ElectionResultArtifactRecord?>(null);

    Task SaveResultArtifactAsync(ElectionResultArtifactRecord resultArtifact) => Task.CompletedTask;

    Task UpdateResultArtifactAsync(ElectionResultArtifactRecord resultArtifact) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionReportPackageRecord>> GetReportPackagesAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionReportPackageRecord>>(Array.Empty<ElectionReportPackageRecord>());

    Task<ElectionReportPackageRecord?> GetLatestReportPackageAsync(ElectionId electionId) =>
        Task.FromResult<ElectionReportPackageRecord?>(null);

    Task<ElectionReportPackageRecord?> GetSealedReportPackageAsync(ElectionId electionId) =>
        Task.FromResult<ElectionReportPackageRecord?>(null);

    Task<ElectionReportPackageRecord?> GetReportPackageAsync(Guid reportPackageId) =>
        Task.FromResult<ElectionReportPackageRecord?>(null);

    Task SaveReportPackageAsync(ElectionReportPackageRecord reportPackage) => Task.CompletedTask;

    Task UpdateReportPackageAsync(ElectionReportPackageRecord reportPackage) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionReportArtifactRecord>> GetReportArtifactsAsync(Guid reportPackageId) =>
        Task.FromResult<IReadOnlyList<ElectionReportArtifactRecord>>(Array.Empty<ElectionReportArtifactRecord>());

    Task SaveReportArtifactAsync(ElectionReportArtifactRecord reportArtifact) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionReportAccessGrantRecord>> GetReportAccessGrantsAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionReportAccessGrantRecord>>(Array.Empty<ElectionReportAccessGrantRecord>());

    Task<IReadOnlyList<ElectionReportAccessGrantRecord>> GetReportAccessGrantsByActorAsync(string actorPublicAddress) =>
        Task.FromResult<IReadOnlyList<ElectionReportAccessGrantRecord>>(Array.Empty<ElectionReportAccessGrantRecord>());

    Task<ElectionReportAccessGrantRecord?> GetReportAccessGrantAsync(ElectionId electionId, string actorPublicAddress) =>
        Task.FromResult<ElectionReportAccessGrantRecord?>(null);

    Task SaveReportAccessGrantAsync(ElectionReportAccessGrantRecord reportAccessGrant) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionRosterEntryRecord>> GetRosterEntriesAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionRosterEntryRecord>>(Array.Empty<ElectionRosterEntryRecord>());

    Task<IReadOnlyList<ElectionRosterEntryRecord>> GetRosterEntriesByLinkedActorAsync(string actorPublicAddress) =>
        Task.FromResult<IReadOnlyList<ElectionRosterEntryRecord>>(Array.Empty<ElectionRosterEntryRecord>());

    Task<ElectionRosterEntryRecord?> GetRosterEntryAsync(ElectionId electionId, string organizationVoterId) =>
        Task.FromResult<ElectionRosterEntryRecord?>(null);

    Task<ElectionRosterEntryRecord?> GetRosterEntryByLinkedActorAsync(ElectionId electionId, string actorPublicAddress) =>
        Task.FromResult<ElectionRosterEntryRecord?>(null);

    Task SaveRosterEntryAsync(ElectionRosterEntryRecord rosterEntry) => Task.CompletedTask;

    Task UpdateRosterEntryAsync(ElectionRosterEntryRecord rosterEntry) => Task.CompletedTask;

    Task DeleteRosterEntriesAsync(ElectionId electionId) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionEligibilityActivationEventRecord>> GetEligibilityActivationEventsAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionEligibilityActivationEventRecord>>(Array.Empty<ElectionEligibilityActivationEventRecord>());

    Task SaveEligibilityActivationEventAsync(ElectionEligibilityActivationEventRecord activationEvent) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionParticipationRecord>> GetParticipationRecordsAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionParticipationRecord>>(Array.Empty<ElectionParticipationRecord>());

    Task<ElectionParticipationRecord?> GetParticipationRecordAsync(ElectionId electionId, string organizationVoterId) =>
        Task.FromResult<ElectionParticipationRecord?>(null);

    Task SaveParticipationRecordAsync(ElectionParticipationRecord participationRecord) => Task.CompletedTask;

    Task UpdateParticipationRecordAsync(ElectionParticipationRecord participationRecord) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionCommitmentRegistrationRecord>> GetCommitmentRegistrationsAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionCommitmentRegistrationRecord>>(Array.Empty<ElectionCommitmentRegistrationRecord>());

    Task<ElectionCommitmentRegistrationRecord?> GetCommitmentRegistrationAsync(ElectionId electionId, string organizationVoterId) =>
        Task.FromResult<ElectionCommitmentRegistrationRecord?>(null);

    Task<ElectionCommitmentRegistrationRecord?> GetCommitmentRegistrationByLinkedActorAsync(ElectionId electionId, string actorPublicAddress) =>
        Task.FromResult<ElectionCommitmentRegistrationRecord?>(null);

    Task SaveCommitmentRegistrationAsync(ElectionCommitmentRegistrationRecord commitmentRegistration) => Task.CompletedTask;

    Task UpdateCommitmentRegistrationAsync(ElectionCommitmentRegistrationRecord commitmentRegistration) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionCheckoffConsumptionRecord>> GetCheckoffConsumptionsAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionCheckoffConsumptionRecord>>(Array.Empty<ElectionCheckoffConsumptionRecord>());

    Task<ElectionCheckoffConsumptionRecord?> GetCheckoffConsumptionAsync(ElectionId electionId, string organizationVoterId) =>
        Task.FromResult<ElectionCheckoffConsumptionRecord?>(null);

    Task SaveCheckoffConsumptionAsync(ElectionCheckoffConsumptionRecord checkoffConsumption) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionEligibilitySnapshotRecord>> GetEligibilitySnapshotsAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionEligibilitySnapshotRecord>>(Array.Empty<ElectionEligibilitySnapshotRecord>());

    Task<ElectionEligibilitySnapshotRecord?> GetEligibilitySnapshotAsync(ElectionId electionId, ElectionEligibilitySnapshotType snapshotType) =>
        Task.FromResult<ElectionEligibilitySnapshotRecord?>(null);

    Task SaveEligibilitySnapshotAsync(ElectionEligibilitySnapshotRecord snapshot) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionAcceptedBallotRecord>> GetAcceptedBallotsAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionAcceptedBallotRecord>>(Array.Empty<ElectionAcceptedBallotRecord>());

    Task<ElectionAcceptedBallotRecord?> GetAcceptedBallotAsync(Guid acceptedBallotId) =>
        Task.FromResult<ElectionAcceptedBallotRecord?>(null);

    Task<ElectionAcceptedBallotRecord?> GetAcceptedBallotByNullifierAsync(ElectionId electionId, string ballotNullifier) =>
        Task.FromResult<ElectionAcceptedBallotRecord?>(null);

    Task SaveAcceptedBallotAsync(ElectionAcceptedBallotRecord acceptedBallot) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionBallotMemPoolRecord>> GetBallotMemPoolEntriesAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionBallotMemPoolRecord>>(Array.Empty<ElectionBallotMemPoolRecord>());

    Task<IReadOnlyList<ElectionId>> GetElectionIdsWithBallotMemPoolEntriesAsync() =>
        Task.FromResult<IReadOnlyList<ElectionId>>(Array.Empty<ElectionId>());

    Task<IReadOnlyList<ElectionId>> GetClosedElectionIdsAwaitingTallyReadyAsync() =>
        Task.FromResult<IReadOnlyList<ElectionId>>(Array.Empty<ElectionId>());

    Task<ElectionBallotMemPoolRecord?> GetBallotMemPoolEntryByAcceptedBallotAsync(ElectionId electionId, Guid acceptedBallotId) =>
        Task.FromResult<ElectionBallotMemPoolRecord?>(null);

    Task SaveBallotMemPoolEntryAsync(ElectionBallotMemPoolRecord ballotMemPoolEntry) => Task.CompletedTask;

    Task DeleteBallotMemPoolEntryAsync(Guid ballotMemPoolEntryId) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionPublishedBallotRecord>> GetPublishedBallotsAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionPublishedBallotRecord>>(Array.Empty<ElectionPublishedBallotRecord>());

    Task<long> GetNextPublishedBallotSequenceAsync(ElectionId electionId) =>
        Task.FromResult(1L);

    Task SavePublishedBallotAsync(ElectionPublishedBallotRecord publishedBallot) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionCastIdempotencyRecord>> GetCastIdempotencyRecordsAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionCastIdempotencyRecord>>(Array.Empty<ElectionCastIdempotencyRecord>());

    Task<ElectionCastIdempotencyRecord?> GetCastIdempotencyRecordAsync(ElectionId electionId, string idempotencyKeyHash) =>
        Task.FromResult<ElectionCastIdempotencyRecord?>(null);

    Task SaveCastIdempotencyRecordAsync(ElectionCastIdempotencyRecord idempotencyRecord) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionPublicationIssueRecord>> GetPublicationIssuesAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionPublicationIssueRecord>>(Array.Empty<ElectionPublicationIssueRecord>());

    Task<ElectionPublicationIssueRecord?> GetPublicationIssueAsync(ElectionId electionId, ElectionPublicationIssueCode issueCode) =>
        Task.FromResult<ElectionPublicationIssueRecord?>(null);

    Task SavePublicationIssueAsync(ElectionPublicationIssueRecord publicationIssue) => Task.CompletedTask;

    Task UpdatePublicationIssueAsync(ElectionPublicationIssueRecord publicationIssue) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionBoundaryArtifactRecord>> GetBoundaryArtifactsAsync(ElectionId electionId);

    Task SaveBoundaryArtifactAsync(ElectionBoundaryArtifactRecord artifact);

    Task UpdateBoundaryArtifactAsync(ElectionBoundaryArtifactRecord artifact);

    Task<IReadOnlyList<ElectionWarningAcknowledgementRecord>> GetWarningAcknowledgementsAsync(ElectionId electionId);

    Task SaveWarningAcknowledgementAsync(ElectionWarningAcknowledgementRecord acknowledgement);

    Task<IReadOnlyList<ElectionTrusteeInvitationRecord>> GetTrusteeInvitationsAsync(ElectionId electionId);

    Task<IReadOnlyList<ElectionTrusteeInvitationRecord>> GetActiveTrusteeInvitationsByActorAsync(string actorPublicAddress) =>
        Task.FromResult<IReadOnlyList<ElectionTrusteeInvitationRecord>>(Array.Empty<ElectionTrusteeInvitationRecord>());

    Task<IReadOnlyList<ElectionTrusteeInvitationRecord>> GetAcceptedTrusteeInvitationsByActorAsync(string actorPublicAddress) =>
        Task.FromResult<IReadOnlyList<ElectionTrusteeInvitationRecord>>(Array.Empty<ElectionTrusteeInvitationRecord>());

    Task<ElectionTrusteeInvitationRecord?> GetTrusteeInvitationAsync(Guid invitationId);

    Task SaveTrusteeInvitationAsync(ElectionTrusteeInvitationRecord invitation);

    Task UpdateTrusteeInvitationAsync(ElectionTrusteeInvitationRecord invitation);

    Task<IReadOnlyList<ElectionGovernedProposalRecord>> GetGovernedProposalsAsync(ElectionId electionId);

    Task<ElectionGovernedProposalRecord?> GetGovernedProposalAsync(Guid proposalId);

    Task<ElectionGovernedProposalRecord?> GetPendingGovernedProposalAsync(ElectionId electionId);

    Task SaveGovernedProposalAsync(ElectionGovernedProposalRecord proposal);

    Task UpdateGovernedProposalAsync(ElectionGovernedProposalRecord proposal);

    Task<IReadOnlyList<ElectionGovernedProposalApprovalRecord>> GetGovernedProposalApprovalsAsync(Guid proposalId);

    Task<ElectionGovernedProposalApprovalRecord?> GetGovernedProposalApprovalAsync(Guid proposalId, string trusteeUserAddress);

    Task SaveGovernedProposalApprovalAsync(ElectionGovernedProposalApprovalRecord approval);

    Task<IReadOnlyList<ElectionCeremonyProfileRecord>> GetCeremonyProfilesAsync();

    Task<ElectionCeremonyProfileRecord?> GetCeremonyProfileAsync(string profileId);

    Task SaveCeremonyProfileAsync(ElectionCeremonyProfileRecord profile);

    Task UpdateCeremonyProfileAsync(ElectionCeremonyProfileRecord profile);

    Task<IReadOnlyList<ElectionCeremonyVersionRecord>> GetCeremonyVersionsAsync(ElectionId electionId);

    Task<ElectionCeremonyVersionRecord?> GetCeremonyVersionAsync(Guid ceremonyVersionId);

    Task<ElectionCeremonyVersionRecord?> GetActiveCeremonyVersionAsync(ElectionId electionId);

    Task SaveCeremonyVersionAsync(ElectionCeremonyVersionRecord version);

    Task UpdateCeremonyVersionAsync(ElectionCeremonyVersionRecord version);

    Task<IReadOnlyList<ElectionCeremonyTranscriptEventRecord>> GetCeremonyTranscriptEventsAsync(Guid ceremonyVersionId);

    Task SaveCeremonyTranscriptEventAsync(ElectionCeremonyTranscriptEventRecord transcriptEvent);

    Task<IReadOnlyList<ElectionCeremonyMessageEnvelopeRecord>> GetCeremonyMessageEnvelopesAsync(Guid ceremonyVersionId);

    Task<IReadOnlyList<ElectionCeremonyMessageEnvelopeRecord>> GetCeremonyMessageEnvelopesForRecipientAsync(Guid ceremonyVersionId, string trusteeUserAddress);

    Task SaveCeremonyMessageEnvelopeAsync(ElectionCeremonyMessageEnvelopeRecord messageEnvelope);

    Task<IReadOnlyList<ElectionCeremonyTrusteeStateRecord>> GetCeremonyTrusteeStatesAsync(Guid ceremonyVersionId);

    Task<ElectionCeremonyTrusteeStateRecord?> GetCeremonyTrusteeStateAsync(Guid ceremonyVersionId, string trusteeUserAddress);

    Task SaveCeremonyTrusteeStateAsync(ElectionCeremonyTrusteeStateRecord trusteeState);

    Task UpdateCeremonyTrusteeStateAsync(ElectionCeremonyTrusteeStateRecord trusteeState);

    Task<IReadOnlyList<ElectionCeremonyShareCustodyRecord>> GetCeremonyShareCustodyRecordsAsync(Guid ceremonyVersionId);

    Task<ElectionCeremonyShareCustodyRecord?> GetCeremonyShareCustodyRecordAsync(Guid ceremonyVersionId, string trusteeUserAddress);

    Task SaveCeremonyShareCustodyRecordAsync(ElectionCeremonyShareCustodyRecord shareCustodyRecord);

    Task UpdateCeremonyShareCustodyRecordAsync(ElectionCeremonyShareCustodyRecord shareCustodyRecord);

    Task<IReadOnlyList<ElectionFinalizationSessionRecord>> GetFinalizationSessionsAsync(ElectionId electionId);

    Task<ElectionFinalizationSessionRecord?> GetFinalizationSessionAsync(Guid finalizationSessionId);

    Task<ElectionFinalizationSessionRecord?> GetActiveFinalizationSessionAsync(ElectionId electionId);

    Task SaveFinalizationSessionAsync(ElectionFinalizationSessionRecord session);

    Task UpdateFinalizationSessionAsync(ElectionFinalizationSessionRecord session);

    Task<IReadOnlyList<ElectionCloseCountingJobRecord>> GetCloseCountingJobsAsync(ElectionId electionId) =>
        Task.FromResult<IReadOnlyList<ElectionCloseCountingJobRecord>>(Array.Empty<ElectionCloseCountingJobRecord>());

    Task<ElectionCloseCountingJobRecord?> GetCloseCountingJobAsync(Guid closeCountingJobId) =>
        Task.FromResult<ElectionCloseCountingJobRecord?>(null);

    Task<ElectionCloseCountingJobRecord?> GetCloseCountingJobBySessionIdAsync(Guid finalizationSessionId) =>
        Task.FromResult<ElectionCloseCountingJobRecord?>(null);

    Task SaveCloseCountingJobAsync(ElectionCloseCountingJobRecord closeCountingJob) => Task.CompletedTask;

    Task UpdateCloseCountingJobAsync(ElectionCloseCountingJobRecord closeCountingJob) => Task.CompletedTask;

    Task<ElectionExecutorSessionKeyEnvelopeRecord?> GetExecutorSessionKeyEnvelopeAsync(Guid closeCountingJobId) =>
        Task.FromResult<ElectionExecutorSessionKeyEnvelopeRecord?>(null);

    Task SaveExecutorSessionKeyEnvelopeAsync(ElectionExecutorSessionKeyEnvelopeRecord envelope) => Task.CompletedTask;

    Task UpdateExecutorSessionKeyEnvelopeAsync(ElectionExecutorSessionKeyEnvelopeRecord envelope) => Task.CompletedTask;

    Task<ElectionAdminOnlyProtectedTallyEnvelopeRecord?> GetAdminOnlyProtectedTallyEnvelopeAsync(ElectionId electionId) =>
        Task.FromResult<ElectionAdminOnlyProtectedTallyEnvelopeRecord?>(null);

    Task SaveAdminOnlyProtectedTallyEnvelopeAsync(ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope) =>
        Task.CompletedTask;

    Task UpdateAdminOnlyProtectedTallyEnvelopeAsync(ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope) =>
        Task.CompletedTask;

    Task<ElectionTallyExecutorLeaseRecord?> GetTallyExecutorLeaseAsync(Guid closeCountingJobId) =>
        Task.FromResult<ElectionTallyExecutorLeaseRecord?>(null);

    Task SaveTallyExecutorLeaseAsync(ElectionTallyExecutorLeaseRecord lease) => Task.CompletedTask;

    Task UpdateTallyExecutorLeaseAsync(ElectionTallyExecutorLeaseRecord lease) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionFinalizationShareRecord>> GetFinalizationSharesAsync(Guid finalizationSessionId);

    Task<ElectionFinalizationShareRecord?> GetAcceptedFinalizationShareAsync(Guid finalizationSessionId, string trusteeUserAddress);

    Task SaveFinalizationShareAsync(ElectionFinalizationShareRecord shareRecord);

    Task UpdateFinalizationShareAsync(ElectionFinalizationShareRecord shareRecord) => Task.CompletedTask;

    Task<IReadOnlyList<ElectionFinalizationReleaseEvidenceRecord>> GetFinalizationReleaseEvidenceRecordsAsync(ElectionId electionId);

    Task<ElectionFinalizationReleaseEvidenceRecord?> GetFinalizationReleaseEvidenceRecordAsync(Guid finalizationSessionId);

    Task SaveFinalizationReleaseEvidenceRecordAsync(ElectionFinalizationReleaseEvidenceRecord releaseEvidenceRecord);
}
