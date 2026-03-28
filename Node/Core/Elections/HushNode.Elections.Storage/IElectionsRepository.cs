using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections.Storage;

public interface IElectionsRepository : IRepository
{
    Task<ElectionRecord?> GetElectionAsync(ElectionId electionId);

    Task<ElectionRecord?> GetElectionForUpdateAsync(ElectionId electionId);

    Task<IReadOnlyList<ElectionRecord>> GetElectionsByOwnerAsync(string ownerPublicAddress);

    Task SaveElectionAsync(ElectionRecord election);

    Task<IReadOnlyList<ElectionDraftSnapshotRecord>> GetDraftSnapshotsAsync(ElectionId electionId);

    Task<ElectionDraftSnapshotRecord?> GetLatestDraftSnapshotAsync(ElectionId electionId);

    Task SaveDraftSnapshotAsync(ElectionDraftSnapshotRecord snapshot);

    Task<ElectionEnvelopeAccessRecord?> GetElectionEnvelopeAccessAsync(ElectionId electionId, string actorPublicAddress);

    Task SaveElectionEnvelopeAccessAsync(ElectionEnvelopeAccessRecord accessRecord);

    Task UpdateElectionEnvelopeAccessAsync(ElectionEnvelopeAccessRecord accessRecord);

    Task<IReadOnlyList<ElectionBoundaryArtifactRecord>> GetBoundaryArtifactsAsync(ElectionId electionId);

    Task SaveBoundaryArtifactAsync(ElectionBoundaryArtifactRecord artifact);

    Task<IReadOnlyList<ElectionWarningAcknowledgementRecord>> GetWarningAcknowledgementsAsync(ElectionId electionId);

    Task SaveWarningAcknowledgementAsync(ElectionWarningAcknowledgementRecord acknowledgement);

    Task<IReadOnlyList<ElectionTrusteeInvitationRecord>> GetTrusteeInvitationsAsync(ElectionId electionId);

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

    Task<IReadOnlyList<ElectionFinalizationShareRecord>> GetFinalizationSharesAsync(Guid finalizationSessionId);

    Task<ElectionFinalizationShareRecord?> GetAcceptedFinalizationShareAsync(Guid finalizationSessionId, string trusteeUserAddress);

    Task SaveFinalizationShareAsync(ElectionFinalizationShareRecord shareRecord);

    Task<IReadOnlyList<ElectionFinalizationReleaseEvidenceRecord>> GetFinalizationReleaseEvidenceRecordsAsync(ElectionId electionId);

    Task<ElectionFinalizationReleaseEvidenceRecord?> GetFinalizationReleaseEvidenceRecordAsync(Guid finalizationSessionId);

    Task SaveFinalizationReleaseEvidenceRecordAsync(ElectionFinalizationReleaseEvidenceRecord releaseEvidenceRecord);
}
