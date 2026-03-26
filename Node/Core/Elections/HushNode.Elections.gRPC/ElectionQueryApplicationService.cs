using HushNetwork.proto;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections.gRPC;

public class ElectionQueryApplicationService(IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider) : IElectionQueryApplicationService
{
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public async Task<GetElectionResponse> GetElectionAsync(HushShared.Elections.Model.ElectionId electionId)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();

        var election = await repository.GetElectionAsync(electionId);
        if (election is null)
        {
            return new GetElectionResponse
            {
                Success = false,
                ErrorMessage = $"Election {electionId} was not found.",
            };
        }

        var latestDraftSnapshot = await repository.GetLatestDraftSnapshotAsync(electionId);
        var warningAcknowledgements = await repository.GetWarningAcknowledgementsAsync(electionId);
        var trusteeInvitations = await repository.GetTrusteeInvitationsAsync(electionId);
        var boundaryArtifacts = await repository.GetBoundaryArtifactsAsync(electionId);
        var governedProposals = await repository.GetGovernedProposalsAsync(electionId);
        var governedProposalApprovals = new List<ElectionGovernedProposalApprovalRecord>();
        foreach (var proposal in governedProposals)
        {
            governedProposalApprovals.AddRange(await repository.GetGovernedProposalApprovalsAsync(proposal.Id));
        }

        var response = new GetElectionResponse
        {
            Success = true,
            Election = election.ToProto(),
            ErrorMessage = string.Empty,
        };

        if (latestDraftSnapshot is not null)
        {
            response.LatestDraftSnapshot = latestDraftSnapshot.ToProto();
        }

        response.WarningAcknowledgements.AddRange(warningAcknowledgements.Select(x => x.ToProto()));
        response.TrusteeInvitations.AddRange(trusteeInvitations.Select(x => x.ToProto()));
        response.BoundaryArtifacts.AddRange(boundaryArtifacts.Select(x => x.ToProto()));
        response.GovernedProposals.AddRange(governedProposals.Select(x => x.ToProto()));
        response.GovernedProposalApprovals.AddRange(governedProposalApprovals
            .OrderBy(x => x.ApprovedAt)
            .Select(x => x.ToProto()));

        return response;
    }

    public async Task<GetElectionsByOwnerResponse> GetElectionsByOwnerAsync(string ownerPublicAddress)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var elections = await repository.GetElectionsByOwnerAsync(ownerPublicAddress);

        var response = new GetElectionsByOwnerResponse();
        response.Elections.AddRange(elections.Select(x => x.ToSummaryProto()));
        return response;
    }
}
