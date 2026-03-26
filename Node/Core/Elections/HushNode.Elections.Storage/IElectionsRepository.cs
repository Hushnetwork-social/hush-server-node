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

    Task<IReadOnlyList<ElectionBoundaryArtifactRecord>> GetBoundaryArtifactsAsync(ElectionId electionId);

    Task SaveBoundaryArtifactAsync(ElectionBoundaryArtifactRecord artifact);

    Task<IReadOnlyList<ElectionWarningAcknowledgementRecord>> GetWarningAcknowledgementsAsync(ElectionId electionId);

    Task SaveWarningAcknowledgementAsync(ElectionWarningAcknowledgementRecord acknowledgement);

    Task<IReadOnlyList<ElectionTrusteeInvitationRecord>> GetTrusteeInvitationsAsync(ElectionId electionId);

    Task<ElectionTrusteeInvitationRecord?> GetTrusteeInvitationAsync(Guid invitationId);

    Task SaveTrusteeInvitationAsync(ElectionTrusteeInvitationRecord invitation);

    Task UpdateTrusteeInvitationAsync(ElectionTrusteeInvitationRecord invitation);
}
