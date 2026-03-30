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
            .SingleOrDefaultAsync();
    }

    public async Task<IReadOnlyList<ElectionRecord>> GetElectionsByOwnerAsync(string ownerPublicAddress) =>
        await Context.Elections
            .Where(x => x.OwnerPublicAddress == ownerPublicAddress)
            .OrderByDescending(x => x.LastUpdatedAt)
            .ToListAsync();

    public async Task<IReadOnlyList<ElectionRecord>> GetElectionsByIdsAsync(IReadOnlyCollection<ElectionId> electionIds)
    {
        if (electionIds.Count == 0)
        {
            return Array.Empty<ElectionRecord>();
        }

        return await Context.Elections
            .Where(x => electionIds.Contains(x.ElectionId))
            .OrderByDescending(x => x.LastUpdatedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ElectionRecord>> SearchElectionsAsync(
        string? searchTerm,
        IReadOnlyCollection<string>? ownerPublicAddresses,
        int limit = 20)
    {
        var normalizedSearchTerm = searchTerm?.Trim() ?? string.Empty;
        var normalizedOwnerAddresses = (ownerPublicAddresses ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (string.IsNullOrWhiteSpace(normalizedSearchTerm) && normalizedOwnerAddresses.Length == 0)
        {
            return Array.Empty<ElectionRecord>();
        }

        var clampedLimit = Math.Clamp(limit, 1, 25);
        var query = Context.Elections.AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedSearchTerm) && normalizedOwnerAddresses.Length > 0)
        {
            var titlePattern = $"%{normalizedSearchTerm}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Title, titlePattern) ||
                normalizedOwnerAddresses.Contains(x.OwnerPublicAddress));
        }
        else if (!string.IsNullOrWhiteSpace(normalizedSearchTerm))
        {
            var titlePattern = $"%{normalizedSearchTerm}%";
            query = query.Where(x => EF.Functions.ILike(x.Title, titlePattern));
        }
        else
        {
            query = query.Where(x => normalizedOwnerAddresses.Contains(x.OwnerPublicAddress));
        }

        return await query
            .OrderByDescending(x => x.LastUpdatedAt)
            .Take(clampedLimit)
            .ToListAsync();
    }

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

    public async Task<ElectionEnvelopeAccessRecord?> GetElectionEnvelopeAccessAsync(
        ElectionId electionId,
        string actorPublicAddress) =>
        await Context.ElectionEnvelopeAccessRecords
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.ActorPublicAddress == actorPublicAddress);

    public async Task SaveElectionEnvelopeAccessAsync(ElectionEnvelopeAccessRecord accessRecord) =>
        await Context.ElectionEnvelopeAccessRecords.AddAsync(accessRecord);

    public async Task UpdateElectionEnvelopeAccessAsync(ElectionEnvelopeAccessRecord accessRecord)
    {
        var existing = await Context.ElectionEnvelopeAccessRecords
            .FirstOrDefaultAsync(x =>
                x.ElectionId == accessRecord.ElectionId &&
                x.ActorPublicAddress == accessRecord.ActorPublicAddress);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(accessRecord);
        }
    }

    public async Task<IReadOnlyList<ElectionResultArtifactRecord>> GetResultArtifactsAsync(ElectionId electionId) =>
        await Context.ElectionResultArtifacts
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.ArtifactKind)
            .ToListAsync();

    public async Task<ElectionResultArtifactRecord?> GetResultArtifactAsync(Guid resultArtifactId) =>
        await Context.ElectionResultArtifacts
            .FirstOrDefaultAsync(x => x.Id == resultArtifactId);

    public async Task<ElectionResultArtifactRecord?> GetResultArtifactAsync(
        ElectionId electionId,
        ElectionResultArtifactKind artifactKind) =>
        await Context.ElectionResultArtifacts
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.ArtifactKind == artifactKind);

    public async Task SaveResultArtifactAsync(ElectionResultArtifactRecord resultArtifact) =>
        await Context.ElectionResultArtifacts.AddAsync(resultArtifact);

    public async Task UpdateResultArtifactAsync(ElectionResultArtifactRecord resultArtifact)
    {
        var existing = await Context.ElectionResultArtifacts
            .FirstOrDefaultAsync(x => x.Id == resultArtifact.Id);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(resultArtifact);
        }
    }

    public async Task<IReadOnlyList<ElectionReportPackageRecord>> GetReportPackagesAsync(ElectionId electionId) =>
        await Context.ElectionReportPackages
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.AttemptNumber)
            .ThenBy(x => x.AttemptedAt)
            .ToListAsync();

    public async Task<ElectionReportPackageRecord?> GetLatestReportPackageAsync(ElectionId electionId) =>
        await Context.ElectionReportPackages
            .Where(x => x.ElectionId == electionId)
            .OrderByDescending(x => x.AttemptNumber)
            .ThenByDescending(x => x.AttemptedAt)
            .FirstOrDefaultAsync();

    public async Task<ElectionReportPackageRecord?> GetSealedReportPackageAsync(ElectionId electionId) =>
        await Context.ElectionReportPackages
            .Where(x =>
                x.ElectionId == electionId &&
                x.Status == ElectionReportPackageStatus.Sealed)
            .OrderByDescending(x => x.SealedAt ?? x.AttemptedAt)
            .ThenByDescending(x => x.AttemptNumber)
            .FirstOrDefaultAsync();

    public async Task<ElectionReportPackageRecord?> GetReportPackageAsync(Guid reportPackageId) =>
        await Context.ElectionReportPackages
            .FirstOrDefaultAsync(x => x.Id == reportPackageId);

    public async Task SaveReportPackageAsync(ElectionReportPackageRecord reportPackage) =>
        await Context.ElectionReportPackages.AddAsync(reportPackage);

    public async Task UpdateReportPackageAsync(ElectionReportPackageRecord reportPackage)
    {
        var existing = await Context.ElectionReportPackages
            .FirstOrDefaultAsync(x => x.Id == reportPackage.Id);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(reportPackage);
        }
    }

    public async Task<IReadOnlyList<ElectionReportArtifactRecord>> GetReportArtifactsAsync(Guid reportPackageId) =>
        await Context.ElectionReportArtifacts
            .Where(x => x.ReportPackageId == reportPackageId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.ArtifactKind)
            .ToListAsync();

    public async Task SaveReportArtifactAsync(ElectionReportArtifactRecord reportArtifact) =>
        await Context.ElectionReportArtifacts.AddAsync(reportArtifact);

    public async Task<IReadOnlyList<ElectionReportAccessGrantRecord>> GetReportAccessGrantsAsync(ElectionId electionId) =>
        await Context.ElectionReportAccessGrants
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.ActorPublicAddress)
            .ThenBy(x => x.GrantRole)
            .ToListAsync();

    public async Task<IReadOnlyList<ElectionReportAccessGrantRecord>> GetReportAccessGrantsByActorAsync(string actorPublicAddress) =>
        await Context.ElectionReportAccessGrants
            .Where(x => x.ActorPublicAddress == actorPublicAddress)
            .OrderByDescending(x => x.GrantedAt)
            .ThenBy(x => x.ElectionId)
            .ToListAsync();

    public async Task<ElectionReportAccessGrantRecord?> GetReportAccessGrantAsync(
        ElectionId electionId,
        string actorPublicAddress) =>
        await Context.ElectionReportAccessGrants
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.ActorPublicAddress == actorPublicAddress);

    public async Task SaveReportAccessGrantAsync(ElectionReportAccessGrantRecord reportAccessGrant) =>
        await Context.ElectionReportAccessGrants.AddAsync(reportAccessGrant);

    public async Task<IReadOnlyList<ElectionRosterEntryRecord>> GetRosterEntriesAsync(ElectionId electionId) =>
        await Context.ElectionRosterEntries
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.OrganizationVoterId)
            .ToListAsync();

    public async Task<IReadOnlyList<ElectionRosterEntryRecord>> GetRosterEntriesByLinkedActorAsync(string actorPublicAddress) =>
        await Context.ElectionRosterEntries
            .Where(x => x.LinkedActorPublicAddress == actorPublicAddress)
            .OrderByDescending(x => x.LastUpdatedAt)
            .ThenBy(x => x.ElectionId)
            .ToListAsync();

    public async Task<ElectionRosterEntryRecord?> GetRosterEntryAsync(
        ElectionId electionId,
        string organizationVoterId) =>
        await Context.ElectionRosterEntries
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.OrganizationVoterId == organizationVoterId);

    public async Task<ElectionRosterEntryRecord?> GetRosterEntryByLinkedActorAsync(
        ElectionId electionId,
        string actorPublicAddress) =>
        await Context.ElectionRosterEntries
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.LinkedActorPublicAddress == actorPublicAddress);

    public async Task SaveRosterEntryAsync(ElectionRosterEntryRecord rosterEntry) =>
        await Context.ElectionRosterEntries.AddAsync(rosterEntry);

    public async Task UpdateRosterEntryAsync(ElectionRosterEntryRecord rosterEntry)
    {
        var existing = await Context.ElectionRosterEntries
            .FirstOrDefaultAsync(x =>
                x.ElectionId == rosterEntry.ElectionId &&
                x.OrganizationVoterId == rosterEntry.OrganizationVoterId);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(rosterEntry);
        }
    }

    public async Task DeleteRosterEntriesAsync(ElectionId electionId)
    {
        var existing = await Context.ElectionRosterEntries
            .Where(x => x.ElectionId == electionId)
            .ToListAsync();

        Context.ElectionRosterEntries.RemoveRange(existing);
    }

    public async Task<IReadOnlyList<ElectionEligibilityActivationEventRecord>> GetEligibilityActivationEventsAsync(ElectionId electionId) =>
        await Context.ElectionEligibilityActivationEvents
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .ToListAsync();

    public async Task SaveEligibilityActivationEventAsync(ElectionEligibilityActivationEventRecord activationEvent) =>
        await Context.ElectionEligibilityActivationEvents.AddAsync(activationEvent);

    public async Task<IReadOnlyList<ElectionParticipationRecord>> GetParticipationRecordsAsync(ElectionId electionId) =>
        await Context.ElectionParticipationRecords
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.OrganizationVoterId)
            .ToListAsync();

    public async Task<ElectionParticipationRecord?> GetParticipationRecordAsync(
        ElectionId electionId,
        string organizationVoterId) =>
        await Context.ElectionParticipationRecords
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.OrganizationVoterId == organizationVoterId);

    public async Task SaveParticipationRecordAsync(ElectionParticipationRecord participationRecord) =>
        await Context.ElectionParticipationRecords.AddAsync(participationRecord);

    public async Task UpdateParticipationRecordAsync(ElectionParticipationRecord participationRecord)
    {
        var existing = await Context.ElectionParticipationRecords
            .FirstOrDefaultAsync(x =>
                x.ElectionId == participationRecord.ElectionId &&
                x.OrganizationVoterId == participationRecord.OrganizationVoterId);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(participationRecord);
        }
    }

    public async Task<IReadOnlyList<ElectionCommitmentRegistrationRecord>> GetCommitmentRegistrationsAsync(ElectionId electionId) =>
        await Context.ElectionCommitmentRegistrations
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.OrganizationVoterId)
            .ToListAsync();

    public async Task<ElectionCommitmentRegistrationRecord?> GetCommitmentRegistrationAsync(
        ElectionId electionId,
        string organizationVoterId) =>
        await Context.ElectionCommitmentRegistrations
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.OrganizationVoterId == organizationVoterId);

    public async Task<ElectionCommitmentRegistrationRecord?> GetCommitmentRegistrationByLinkedActorAsync(
        ElectionId electionId,
        string actorPublicAddress) =>
        await Context.ElectionCommitmentRegistrations
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.LinkedActorPublicAddress == actorPublicAddress);

    public async Task SaveCommitmentRegistrationAsync(ElectionCommitmentRegistrationRecord commitmentRegistration) =>
        await Context.ElectionCommitmentRegistrations.AddAsync(commitmentRegistration);

    public async Task UpdateCommitmentRegistrationAsync(ElectionCommitmentRegistrationRecord commitmentRegistration)
    {
        var existing = await Context.ElectionCommitmentRegistrations
            .FirstOrDefaultAsync(x =>
                x.ElectionId == commitmentRegistration.ElectionId &&
                x.OrganizationVoterId == commitmentRegistration.OrganizationVoterId);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(commitmentRegistration);
        }
    }

    public async Task<IReadOnlyList<ElectionCheckoffConsumptionRecord>> GetCheckoffConsumptionsAsync(ElectionId electionId) =>
        await Context.ElectionCheckoffConsumptions
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.ConsumedAt)
            .ToListAsync();

    public async Task<ElectionCheckoffConsumptionRecord?> GetCheckoffConsumptionAsync(
        ElectionId electionId,
        string organizationVoterId) =>
        await Context.ElectionCheckoffConsumptions
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.OrganizationVoterId == organizationVoterId);

    public async Task SaveCheckoffConsumptionAsync(ElectionCheckoffConsumptionRecord checkoffConsumption) =>
        await Context.ElectionCheckoffConsumptions.AddAsync(checkoffConsumption);

    public async Task<IReadOnlyList<ElectionEligibilitySnapshotRecord>> GetEligibilitySnapshotsAsync(ElectionId electionId) =>
        await Context.ElectionEligibilitySnapshots
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.SnapshotType)
            .ToListAsync();

    public async Task<ElectionEligibilitySnapshotRecord?> GetEligibilitySnapshotAsync(
        ElectionId electionId,
        ElectionEligibilitySnapshotType snapshotType) =>
        await Context.ElectionEligibilitySnapshots
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.SnapshotType == snapshotType);

    public async Task SaveEligibilitySnapshotAsync(ElectionEligibilitySnapshotRecord snapshot) =>
        await Context.ElectionEligibilitySnapshots.AddAsync(snapshot);

    public async Task<IReadOnlyList<ElectionAcceptedBallotRecord>> GetAcceptedBallotsAsync(ElectionId electionId) =>
        await Context.ElectionAcceptedBallots
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.AcceptedAt)
            .ToListAsync();

    public async Task<ElectionAcceptedBallotRecord?> GetAcceptedBallotAsync(Guid acceptedBallotId) =>
        await Context.ElectionAcceptedBallots
            .FirstOrDefaultAsync(x => x.Id == acceptedBallotId);

    public async Task<ElectionAcceptedBallotRecord?> GetAcceptedBallotByNullifierAsync(
        ElectionId electionId,
        string ballotNullifier) =>
        await Context.ElectionAcceptedBallots
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.BallotNullifier == ballotNullifier);

    public async Task SaveAcceptedBallotAsync(ElectionAcceptedBallotRecord acceptedBallot) =>
        await Context.ElectionAcceptedBallots.AddAsync(acceptedBallot);

    public async Task<IReadOnlyList<ElectionBallotMemPoolRecord>> GetBallotMemPoolEntriesAsync(ElectionId electionId) =>
        await Context.ElectionBallotMemPoolEntries
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.QueuedAt)
            .ThenBy(x => x.Id)
            .ToListAsync();

    public async Task<IReadOnlyList<ElectionId>> GetElectionIdsWithBallotMemPoolEntriesAsync() =>
        await Context.ElectionBallotMemPoolEntries
            .Select(x => x.ElectionId)
            .Distinct()
            .ToListAsync();

    public async Task<IReadOnlyList<ElectionId>> GetClosedElectionIdsAwaitingTallyReadyAsync() =>
        await Context.Elections
            .Where(x =>
                x.LifecycleState == ElectionLifecycleState.Closed &&
                x.TallyReadyAt == null)
            .Select(x => x.ElectionId)
            .ToListAsync();

    public async Task<ElectionBallotMemPoolRecord?> GetBallotMemPoolEntryByAcceptedBallotAsync(
        ElectionId electionId,
        Guid acceptedBallotId) =>
        await Context.ElectionBallotMemPoolEntries
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.AcceptedBallotId == acceptedBallotId);

    public async Task SaveBallotMemPoolEntryAsync(ElectionBallotMemPoolRecord ballotMemPoolEntry) =>
        await Context.ElectionBallotMemPoolEntries.AddAsync(ballotMemPoolEntry);

    public async Task DeleteBallotMemPoolEntryAsync(Guid ballotMemPoolEntryId)
    {
        var existing = await Context.ElectionBallotMemPoolEntries
            .FirstOrDefaultAsync(x => x.Id == ballotMemPoolEntryId);
        if (existing is not null)
        {
            Context.ElectionBallotMemPoolEntries.Remove(existing);
        }
    }

    public async Task<IReadOnlyList<ElectionPublishedBallotRecord>> GetPublishedBallotsAsync(ElectionId electionId) =>
        await Context.ElectionPublishedBallots
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.PublicationSequence)
            .ToListAsync();

    public async Task<long> GetNextPublishedBallotSequenceAsync(ElectionId electionId)
    {
        var currentMax = await Context.ElectionPublishedBallots
            .Where(x => x.ElectionId == electionId)
            .Select(x => (long?)x.PublicationSequence)
            .MaxAsync();

        return (currentMax ?? 0) + 1;
    }

    public async Task SavePublishedBallotAsync(ElectionPublishedBallotRecord publishedBallot) =>
        await Context.ElectionPublishedBallots.AddAsync(publishedBallot);

    public async Task<IReadOnlyList<ElectionCastIdempotencyRecord>> GetCastIdempotencyRecordsAsync(ElectionId electionId) =>
        await Context.ElectionCastIdempotencyRecords
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.RecordedAt)
            .ToListAsync();

    public async Task<ElectionCastIdempotencyRecord?> GetCastIdempotencyRecordAsync(
        ElectionId electionId,
        string idempotencyKeyHash) =>
        await Context.ElectionCastIdempotencyRecords
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.IdempotencyKeyHash == idempotencyKeyHash);

    public async Task SaveCastIdempotencyRecordAsync(ElectionCastIdempotencyRecord idempotencyRecord) =>
        await Context.ElectionCastIdempotencyRecords.AddAsync(idempotencyRecord);

    public async Task<IReadOnlyList<ElectionPublicationIssueRecord>> GetPublicationIssuesAsync(ElectionId electionId) =>
        await Context.ElectionPublicationIssues
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.IssueCode)
            .ToListAsync();

    public async Task<ElectionPublicationIssueRecord?> GetPublicationIssueAsync(
        ElectionId electionId,
        ElectionPublicationIssueCode issueCode) =>
        await Context.ElectionPublicationIssues
            .FirstOrDefaultAsync(x =>
                x.ElectionId == electionId &&
                x.IssueCode == issueCode);

    public async Task SavePublicationIssueAsync(ElectionPublicationIssueRecord publicationIssue) =>
        await Context.ElectionPublicationIssues.AddAsync(publicationIssue);

    public async Task UpdatePublicationIssueAsync(ElectionPublicationIssueRecord publicationIssue)
    {
        var existing = await Context.ElectionPublicationIssues
            .FirstOrDefaultAsync(x =>
                x.ElectionId == publicationIssue.ElectionId &&
                x.IssueCode == publicationIssue.IssueCode);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(publicationIssue);
        }
    }

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

    public async Task<IReadOnlyList<ElectionTrusteeInvitationRecord>> GetAcceptedTrusteeInvitationsByActorAsync(string actorPublicAddress) =>
        await Context.ElectionTrusteeInvitations
            .Where(x =>
                x.TrusteeUserAddress == actorPublicAddress &&
                x.Status == ElectionTrusteeInvitationStatus.Accepted)
            .OrderByDescending(x => x.RespondedAt ?? x.SentAt)
            .ThenBy(x => x.ElectionId)
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

    public async Task<IReadOnlyList<ElectionCeremonyProfileRecord>> GetCeremonyProfilesAsync() =>
        await Context.ElectionCeremonyProfiles
            .OrderBy(x => x.RequiredApprovalCount)
            .ThenBy(x => x.TrusteeCount)
            .ThenBy(x => x.ProfileId)
            .ToListAsync();

    public async Task<ElectionCeremonyProfileRecord?> GetCeremonyProfileAsync(string profileId) =>
        await Context.ElectionCeremonyProfiles
            .FirstOrDefaultAsync(x => x.ProfileId == profileId);

    public async Task SaveCeremonyProfileAsync(ElectionCeremonyProfileRecord profile) =>
        await Context.ElectionCeremonyProfiles.AddAsync(profile);

    public async Task UpdateCeremonyProfileAsync(ElectionCeremonyProfileRecord profile)
    {
        var existing = await Context.ElectionCeremonyProfiles
            .FirstOrDefaultAsync(x => x.ProfileId == profile.ProfileId);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(profile);
        }
    }

    public async Task<IReadOnlyList<ElectionCeremonyVersionRecord>> GetCeremonyVersionsAsync(ElectionId electionId) =>
        await Context.ElectionCeremonyVersions
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.VersionNumber)
            .ToListAsync();

    public async Task<ElectionCeremonyVersionRecord?> GetCeremonyVersionAsync(Guid ceremonyVersionId) =>
        await Context.ElectionCeremonyVersions
            .FirstOrDefaultAsync(x => x.Id == ceremonyVersionId);

    public async Task<ElectionCeremonyVersionRecord?> GetActiveCeremonyVersionAsync(ElectionId electionId)
    {
        var activeVersions = await Context.ElectionCeremonyVersions
            .Where(x =>
                x.ElectionId == electionId &&
                x.Status != ElectionCeremonyVersionStatus.Superseded)
            .OrderBy(x => x.VersionNumber)
            .ToListAsync();

        return activeVersions.Count switch
        {
            0 => null,
            1 => activeVersions[0],
            _ => throw new InvalidOperationException(
                $"Election {electionId} has multiple active ceremony versions, which violates the FEAT-097 invariant."),
        };
    }

    public async Task SaveCeremonyVersionAsync(ElectionCeremonyVersionRecord version) =>
        await Context.ElectionCeremonyVersions.AddAsync(version);

    public async Task UpdateCeremonyVersionAsync(ElectionCeremonyVersionRecord version)
    {
        var existing = await Context.ElectionCeremonyVersions
            .FirstOrDefaultAsync(x => x.Id == version.Id);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(version);
        }
    }

    public async Task<IReadOnlyList<ElectionCeremonyTranscriptEventRecord>> GetCeremonyTranscriptEventsAsync(Guid ceremonyVersionId) =>
        await Context.ElectionCeremonyTranscriptEvents
            .Where(x => x.CeremonyVersionId == ceremonyVersionId)
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .ToListAsync();

    public async Task SaveCeremonyTranscriptEventAsync(ElectionCeremonyTranscriptEventRecord transcriptEvent) =>
        await Context.ElectionCeremonyTranscriptEvents.AddAsync(transcriptEvent);

    public async Task<IReadOnlyList<ElectionCeremonyMessageEnvelopeRecord>> GetCeremonyMessageEnvelopesAsync(Guid ceremonyVersionId) =>
        await Context.ElectionCeremonyMessageEnvelopes
            .Where(x => x.CeremonyVersionId == ceremonyVersionId)
            .OrderBy(x => x.SubmittedAt)
            .ThenBy(x => x.Id)
            .ToListAsync();

    public async Task<IReadOnlyList<ElectionCeremonyMessageEnvelopeRecord>> GetCeremonyMessageEnvelopesForRecipientAsync(
        Guid ceremonyVersionId,
        string trusteeUserAddress) =>
        await Context.ElectionCeremonyMessageEnvelopes
            .Where(x =>
                x.CeremonyVersionId == ceremonyVersionId &&
                x.RecipientTrusteeUserAddress == trusteeUserAddress)
            .OrderBy(x => x.SubmittedAt)
            .ThenBy(x => x.Id)
            .ToListAsync();

    public async Task SaveCeremonyMessageEnvelopeAsync(ElectionCeremonyMessageEnvelopeRecord messageEnvelope) =>
        await Context.ElectionCeremonyMessageEnvelopes.AddAsync(messageEnvelope);

    public async Task<IReadOnlyList<ElectionCeremonyTrusteeStateRecord>> GetCeremonyTrusteeStatesAsync(Guid ceremonyVersionId) =>
        await Context.ElectionCeremonyTrusteeStates
            .Where(x => x.CeremonyVersionId == ceremonyVersionId)
            .OrderBy(x => x.TrusteeUserAddress)
            .ToListAsync();

    public async Task<ElectionCeremonyTrusteeStateRecord?> GetCeremonyTrusteeStateAsync(
        Guid ceremonyVersionId,
        string trusteeUserAddress) =>
        await Context.ElectionCeremonyTrusteeStates
            .FirstOrDefaultAsync(x =>
                x.CeremonyVersionId == ceremonyVersionId &&
                x.TrusteeUserAddress == trusteeUserAddress);

    public async Task SaveCeremonyTrusteeStateAsync(ElectionCeremonyTrusteeStateRecord trusteeState) =>
        await Context.ElectionCeremonyTrusteeStates.AddAsync(trusteeState);

    public async Task UpdateCeremonyTrusteeStateAsync(ElectionCeremonyTrusteeStateRecord trusteeState)
    {
        var existing = await Context.ElectionCeremonyTrusteeStates
            .FirstOrDefaultAsync(x => x.Id == trusteeState.Id);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(trusteeState);
        }
    }

    public async Task<IReadOnlyList<ElectionCeremonyShareCustodyRecord>> GetCeremonyShareCustodyRecordsAsync(Guid ceremonyVersionId) =>
        await Context.ElectionCeremonyShareCustodyRecords
            .Where(x => x.CeremonyVersionId == ceremonyVersionId)
            .OrderBy(x => x.TrusteeUserAddress)
            .ToListAsync();

    public async Task<ElectionCeremonyShareCustodyRecord?> GetCeremonyShareCustodyRecordAsync(
        Guid ceremonyVersionId,
        string trusteeUserAddress) =>
        await Context.ElectionCeremonyShareCustodyRecords
            .FirstOrDefaultAsync(x =>
                x.CeremonyVersionId == ceremonyVersionId &&
                x.TrusteeUserAddress == trusteeUserAddress);

    public async Task SaveCeremonyShareCustodyRecordAsync(ElectionCeremonyShareCustodyRecord shareCustodyRecord) =>
        await Context.ElectionCeremonyShareCustodyRecords.AddAsync(shareCustodyRecord);

    public async Task UpdateCeremonyShareCustodyRecordAsync(ElectionCeremonyShareCustodyRecord shareCustodyRecord)
    {
        var existing = await Context.ElectionCeremonyShareCustodyRecords
            .FirstOrDefaultAsync(x => x.Id == shareCustodyRecord.Id);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(shareCustodyRecord);
        }
    }

    public async Task<IReadOnlyList<ElectionFinalizationSessionRecord>> GetFinalizationSessionsAsync(ElectionId electionId) =>
        await Context.ElectionFinalizationSessions
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

    public async Task<ElectionFinalizationSessionRecord?> GetFinalizationSessionAsync(Guid finalizationSessionId) =>
        await Context.ElectionFinalizationSessions
            .FirstOrDefaultAsync(x => x.Id == finalizationSessionId);

    public async Task<ElectionFinalizationSessionRecord?> GetActiveFinalizationSessionAsync(ElectionId electionId)
    {
        var activeSessions = await Context.ElectionFinalizationSessions
            .Where(x =>
                x.ElectionId == electionId &&
                x.Status != ElectionFinalizationSessionStatus.Completed)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        return activeSessions.Count switch
        {
            0 => null,
            1 => activeSessions[0],
            _ => throw new InvalidOperationException(
                $"Election {electionId} has multiple active finalization sessions, which violates the FEAT-098 invariant."),
        };
    }

    public async Task SaveFinalizationSessionAsync(ElectionFinalizationSessionRecord session) =>
        await Context.ElectionFinalizationSessions.AddAsync(session);

    public async Task UpdateFinalizationSessionAsync(ElectionFinalizationSessionRecord session)
    {
        var existing = await Context.ElectionFinalizationSessions
            .FirstOrDefaultAsync(x => x.Id == session.Id);

        if (existing is not null)
        {
            Context.Entry(existing).CurrentValues.SetValues(session);
        }
    }

    public async Task<IReadOnlyList<ElectionFinalizationShareRecord>> GetFinalizationSharesAsync(Guid finalizationSessionId) =>
        await Context.ElectionFinalizationShares
            .Where(x => x.FinalizationSessionId == finalizationSessionId)
            .OrderBy(x => x.SubmittedAt)
            .ThenBy(x => x.Id)
            .ToListAsync();

    public async Task<ElectionFinalizationShareRecord?> GetAcceptedFinalizationShareAsync(
        Guid finalizationSessionId,
        string trusteeUserAddress) =>
        await Context.ElectionFinalizationShares
            .Where(x =>
                x.FinalizationSessionId == finalizationSessionId &&
                x.TrusteeUserAddress == trusteeUserAddress &&
                x.Status == ElectionFinalizationShareStatus.Accepted)
            .OrderByDescending(x => x.SubmittedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

    public async Task SaveFinalizationShareAsync(ElectionFinalizationShareRecord shareRecord) =>
        await Context.ElectionFinalizationShares.AddAsync(shareRecord);

    public async Task<IReadOnlyList<ElectionFinalizationReleaseEvidenceRecord>> GetFinalizationReleaseEvidenceRecordsAsync(ElectionId electionId) =>
        await Context.ElectionFinalizationReleaseEvidenceRecords
            .Where(x => x.ElectionId == electionId)
            .OrderBy(x => x.CompletedAt)
            .ToListAsync();

    public async Task<ElectionFinalizationReleaseEvidenceRecord?> GetFinalizationReleaseEvidenceRecordAsync(Guid finalizationSessionId) =>
        await Context.ElectionFinalizationReleaseEvidenceRecords
            .FirstOrDefaultAsync(x => x.FinalizationSessionId == finalizationSessionId);

    public async Task SaveFinalizationReleaseEvidenceRecordAsync(ElectionFinalizationReleaseEvidenceRecord releaseEvidenceRecord) =>
        await Context.ElectionFinalizationReleaseEvidenceRecords.AddAsync(releaseEvidenceRecord);
}
