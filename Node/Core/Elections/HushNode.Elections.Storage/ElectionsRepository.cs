using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections.Storage;

public class ElectionsRepository : RepositoryBase<ElectionsDbContext>, IElectionsRepository
{
    public async Task<ElectionRecord?> GetElectionAsync(ElectionId electionId) =>
        await Context.Elections.FirstOrDefaultAsync(x => x.ElectionId == electionId);

    public async Task<ElectionRecord?> GetElectionForUpdateAsync(ElectionId electionId)
    {
        var electionIdValue = electionId.ToString();
        return await Context.Elections
            .FromSqlRaw(
                @"SELECT * FROM ""Elections"".""ElectionRecord""
                  WHERE ""ElectionId"" = {0} FOR UPDATE",
                electionIdValue)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<ElectionRecord>> GetElectionsByOwnerAsync(string ownerPublicAddress) =>
        await Context.Elections
            .Where(x => x.OwnerPublicAddress == ownerPublicAddress)
            .OrderByDescending(x => x.LastUpdatedAt)
            .ToListAsync();

    public async Task SaveElectionAsync(ElectionRecord election)
    {
        var existing = await Context.Elections
            .FirstOrDefaultAsync(x => x.ElectionId == election.ElectionId);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(election);
        }
        else
        {
            await Context.Elections.AddAsync(election);
        }
    }

    public async Task<IReadOnlyList<ElectionDraftSnapshotRecord>> GetDraftSnapshotsAsync(ElectionId electionId) =>
        await Context.ElectionDraftSnapshots
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.DraftRevision)
            .ThenBy(x => x.RecordedAt)
            .ToListAsync();

    public async Task<ElectionDraftSnapshotRecord?> GetLatestDraftSnapshotAsync(ElectionId electionId) =>
        await Context.ElectionDraftSnapshots
            .Where(x => x.ElectionId == electionId)
            .OrderByDescending(x => x.DraftRevision)
            .ThenByDescending(x => x.RecordedAt)
            .FirstOrDefaultAsync();

    public async Task SaveDraftSnapshotAsync(ElectionDraftSnapshotRecord snapshot) =>
        await Context.ElectionDraftSnapshots.AddAsync(snapshot);

    public async Task<IReadOnlyList<ElectionBoundaryArtifactRecord>> GetBoundaryArtifactsAsync(ElectionId electionId) =>
        await Context.ElectionBoundaryArtifacts
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.RecordedAt)
            .ToListAsync();

    public async Task SaveBoundaryArtifactAsync(ElectionBoundaryArtifactRecord artifact) =>
        await Context.ElectionBoundaryArtifacts.AddAsync(artifact);

    public async Task<IReadOnlyList<ElectionWarningAcknowledgementRecord>> GetWarningAcknowledgementsAsync(ElectionId electionId) =>
        await Context.ElectionWarningAcknowledgements
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.DraftRevision)
            .ThenBy(x => x.AcknowledgedAt)
            .ToListAsync();

    public async Task SaveWarningAcknowledgementAsync(ElectionWarningAcknowledgementRecord acknowledgement) =>
        await Context.ElectionWarningAcknowledgements.AddAsync(acknowledgement);

    public async Task<IReadOnlyList<ElectionTrusteeInvitationRecord>> GetTrusteeInvitationsAsync(ElectionId electionId) =>
        await Context.ElectionTrusteeInvitations
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.SentAt)
            .ToListAsync();

    public async Task<ElectionTrusteeInvitationRecord?> GetTrusteeInvitationAsync(Guid invitationId) =>
        await Context.ElectionTrusteeInvitations
            .FirstOrDefaultAsync(x => x.Id == invitationId);

    public async Task SaveTrusteeInvitationAsync(ElectionTrusteeInvitationRecord invitation) =>
        await Context.ElectionTrusteeInvitations.AddAsync(invitation);

    public async Task UpdateTrusteeInvitationAsync(ElectionTrusteeInvitationRecord invitation)
    {
        var existing = await Context.ElectionTrusteeInvitations
            .FirstOrDefaultAsync(x => x.Id == invitation.Id);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(invitation);
        }
    }

    public async Task<IReadOnlyList<ElectionGovernedProposalRecord>> GetGovernedProposalsAsync(ElectionId electionId) =>
        await Context.ElectionGovernedProposals
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

    public async Task<ElectionGovernedProposalRecord?> GetGovernedProposalAsync(Guid proposalId) =>
        await Context.ElectionGovernedProposals
            .FirstOrDefaultAsync(x => x.Id == proposalId);

    public async Task<ElectionGovernedProposalRecord?> GetPendingGovernedProposalAsync(ElectionId electionId)
    {
        var pendingProposals = await Context.ElectionGovernedProposals
            .Where(x =>
                x.ElectionId == electionId &&
                x.ExecutionStatus != ElectionGovernedProposalExecutionStatus.ExecutionSucceeded)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        return pendingProposals.Count switch
        {
            0 => null,
            1 => pendingProposals[0],
            _ => throw new InvalidOperationException(
                $"Election {electionId} has multiple pending governed proposals, which violates the FEAT-096 invariant."),
        };
    }

    public async Task SaveGovernedProposalAsync(ElectionGovernedProposalRecord proposal) =>
        await Context.ElectionGovernedProposals.AddAsync(proposal);

    public async Task UpdateGovernedProposalAsync(ElectionGovernedProposalRecord proposal)
    {
        var existing = await Context.ElectionGovernedProposals
            .FirstOrDefaultAsync(x => x.Id == proposal.Id);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(proposal);
        }
    }

    public async Task<IReadOnlyList<ElectionGovernedProposalApprovalRecord>> GetGovernedProposalApprovalsAsync(Guid proposalId) =>
        await Context.ElectionGovernedProposalApprovals
            .Where(x => x.ProposalId == proposalId)
            .OrderBy(x => x.ApprovedAt)
            .ToListAsync();

    public async Task<ElectionGovernedProposalApprovalRecord?> GetGovernedProposalApprovalAsync(
        Guid proposalId,
        string trusteeUserAddress) =>
        await Context.ElectionGovernedProposalApprovals
            .FirstOrDefaultAsync(x =>
                x.ProposalId == proposalId &&
                x.TrusteeUserAddress == trusteeUserAddress);

    public async Task SaveGovernedProposalApprovalAsync(ElectionGovernedProposalApprovalRecord approval) =>
        await Context.ElectionGovernedProposalApprovals.AddAsync(approval);
}
