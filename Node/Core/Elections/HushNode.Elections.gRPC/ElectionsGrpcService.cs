using Grpc.Core;
using HushNetwork.proto;
using Microsoft.Extensions.Logging;
using Domain = HushNode.Elections;
using Proto = HushNetwork.proto;

namespace HushNode.Elections.gRPC;

public class ElectionsGrpcService(
    Domain.IElectionLifecycleService lifecycleService,
    IElectionQueryApplicationService queryApplicationService,
    ILogger<ElectionsGrpcService> logger)
    : Proto.HushElections.HushElectionsBase
{
    private readonly Domain.IElectionLifecycleService _lifecycleService = lifecycleService;
    private readonly IElectionQueryApplicationService _queryApplicationService = queryApplicationService;
    private readonly ILogger<ElectionsGrpcService> _logger = logger;

    public override Task<ElectionCommandResponse> CreateElectionDraft(Proto.CreateElectionDraftRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for CreateElectionDraft."));

    public override Task<ElectionCommandResponse> UpdateElectionDraft(Proto.UpdateElectionDraftRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for UpdateElectionDraft."));

    public override Task<ElectionCommandResponse> InviteElectionTrustee(Proto.InviteElectionTrusteeRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for InviteElectionTrustee."));

    public override Task<ElectionCommandResponse> CreateElectionReportAccessGrant(Proto.CreateElectionReportAccessGrantRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for CreateElectionReportAccessGrant."));

    public override Task<ElectionCommandResponse> AcceptElectionTrusteeInvitation(Proto.ResolveElectionTrusteeInvitationRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for AcceptElectionTrusteeInvitation."));

    public override Task<ElectionCommandResponse> RejectElectionTrusteeInvitation(Proto.ResolveElectionTrusteeInvitationRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for RejectElectionTrusteeInvitation."));

    public override Task<ElectionCommandResponse> RevokeElectionTrusteeInvitation(Proto.ResolveElectionTrusteeInvitationRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for RevokeElectionTrusteeInvitation."));

    public override Task<ElectionCommandResponse> StartElectionCeremony(Proto.StartElectionCeremonyRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for StartElectionCeremony."));

    public override Task<ElectionCommandResponse> RestartElectionCeremony(Proto.RestartElectionCeremonyRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for RestartElectionCeremony."));

    public override Task<ElectionCommandResponse> PublishElectionCeremonyTransportKey(Proto.PublishElectionCeremonyTransportKeyRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for PublishElectionCeremonyTransportKey."));

    public override Task<ElectionCommandResponse> JoinElectionCeremony(Proto.JoinElectionCeremonyRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for JoinElectionCeremony."));

    public override Task<ElectionCommandResponse> RecordElectionCeremonySelfTestSuccess(Proto.RecordElectionCeremonySelfTestRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for RecordElectionCeremonySelfTestSuccess."));

    public override Task<ElectionCommandResponse> SubmitElectionCeremonyMaterial(Proto.SubmitElectionCeremonyMaterialRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for SubmitElectionCeremonyMaterial."));

    public override Task<ElectionCommandResponse> RecordElectionCeremonyValidationFailure(Proto.RecordElectionCeremonyValidationFailureRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for RecordElectionCeremonyValidationFailure."));

    public override Task<ElectionCommandResponse> CompleteElectionCeremonyTrustee(Proto.CompleteElectionCeremonyTrusteeRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for CompleteElectionCeremonyTrustee."));

    public override Task<ElectionCommandResponse> RecordElectionCeremonyShareExport(Proto.RecordElectionCeremonyShareExportRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for RecordElectionCeremonyShareExport."));

    public override Task<ElectionCommandResponse> RecordElectionCeremonyShareImport(Proto.RecordElectionCeremonyShareImportRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for RecordElectionCeremonyShareImport."));

    public override async Task<GetElectionOpenReadinessResponse> GetElectionOpenReadiness(GetElectionOpenReadinessRequest request, ServerCallContext context)
    {
        try
        {
            var result = await _lifecycleService.EvaluateOpenReadinessAsync(new HushNode.Elections.EvaluateElectionOpenReadinessRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                request.RequiredWarningCodes.Select(x => (HushShared.Elections.Model.ElectionWarningCode)(int)x).ToArray()));

            return result.ToProto();
        }
        catch (FormatException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(GetElectionOpenReadiness));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to evaluate election open readiness."));
        }
    }

    public override Task<ElectionCommandResponse> StartElectionGovernedProposal(Proto.StartElectionGovernedProposalRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for StartElectionGovernedProposal."));

    public override Task<ElectionCommandResponse> ApproveElectionGovernedProposal(Proto.ApproveElectionGovernedProposalRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for ApproveElectionGovernedProposal."));

    public override Task<ElectionCommandResponse> RetryElectionGovernedProposalExecution(Proto.RetryElectionGovernedProposalExecutionRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for RetryElectionGovernedProposalExecution."));

    public override Task<ElectionCommandResponse> OpenElection(Proto.OpenElectionRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for OpenElection."));

    public override Task<ElectionCommandResponse> CloseElection(Proto.CloseElectionRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for CloseElection."));

    public override Task<ElectionCommandResponse> FinalizeElection(Proto.FinalizeElectionRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for FinalizeElection."));

    public override Task<Proto.RegisterElectionVotingCommitmentResponse> RegisterElectionVotingCommitment(
        Proto.RegisterElectionVotingCommitmentRequest request,
        ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for RegisterElectionVotingCommitment."));

    public override Task<Proto.AcceptElectionBallotCastResponse> AcceptElectionBallotCast(
        Proto.AcceptElectionBallotCastRequest request,
        ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for AcceptElectionBallotCast."));

    public override async Task<GetElectionResponse> GetElection(GetElectionRequest request, ServerCallContext context)
    {
        try
        {
            return await _queryApplicationService.GetElectionAsync(ElectionGrpcMappings.ParseElectionId(request.ElectionId));
        }
        catch (FormatException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(GetElection));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to fetch election."));
        }
    }

    public override async Task<GetElectionHubViewResponse> GetElectionHubView(GetElectionHubViewRequest request, ServerCallContext context)
    {
        try
        {
            return await _queryApplicationService.GetElectionHubViewAsync(request.ActorPublicAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(GetElectionHubView));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to fetch election hub view."));
        }
    }

    public override async Task<SearchElectionDirectoryResponse> SearchElectionDirectory(
        SearchElectionDirectoryRequest request,
        ServerCallContext context)
    {
        try
        {
            return await _queryApplicationService.SearchElectionDirectoryAsync(
                request.SearchTerm,
                request.OwnerPublicAddresses,
                request.Limit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(SearchElectionDirectory));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to search election directory."));
        }
    }

    public override async Task<GetElectionEligibilityViewResponse> GetElectionEligibilityView(
        GetElectionEligibilityViewRequest request,
        ServerCallContext context)
    {
        try
        {
            return await _queryApplicationService.GetElectionEligibilityViewAsync(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                request.ActorPublicAddress);
        }
        catch (FormatException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(GetElectionEligibilityView));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to fetch election eligibility view."));
        }
    }

    public override async Task<GetElectionVotingViewResponse> GetElectionVotingView(
        GetElectionVotingViewRequest request,
        ServerCallContext context)
    {
        try
        {
            return await _queryApplicationService.GetElectionVotingViewAsync(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                request.ActorPublicAddress,
                request.SubmissionIdempotencyKey);
        }
        catch (FormatException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(GetElectionVotingView));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to fetch election voting view."));
        }
    }

    public override async Task<VerifyElectionReceiptResponse> VerifyElectionReceipt(
        VerifyElectionReceiptRequest request,
        ServerCallContext context)
    {
        try
        {
            return await _queryApplicationService.VerifyElectionReceiptAsync(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                request.ActorPublicAddress,
                request.ReceiptId,
                request.AcceptanceId,
                request.ServerProof);
        }
        catch (FormatException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(VerifyElectionReceipt));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to verify election receipt."));
        }
    }

    public override async Task<GetElectionEnvelopeAccessResponse> GetElectionEnvelopeAccess(GetElectionEnvelopeAccessRequest request, ServerCallContext context)
    {
        try
        {
            return await _queryApplicationService.GetElectionEnvelopeAccessAsync(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                request.ActorPublicAddress);
        }
        catch (FormatException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(GetElectionEnvelopeAccess));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to fetch election envelope access."));
        }
    }

    public override async Task<GetElectionResultViewResponse> GetElectionResultView(GetElectionResultViewRequest request, ServerCallContext context)
    {
        try
        {
            return await _queryApplicationService.GetElectionResultViewAsync(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                request.ActorPublicAddress);
        }
        catch (FormatException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(GetElectionResultView));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to fetch election result view."));
        }
    }

    public override async Task<GetElectionReportAccessGrantsResponse> GetElectionReportAccessGrants(
        GetElectionReportAccessGrantsRequest request,
        ServerCallContext context)
    {
        try
        {
            return await _queryApplicationService.GetElectionReportAccessGrantsAsync(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                request.ActorPublicAddress);
        }
        catch (FormatException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(GetElectionReportAccessGrants));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to fetch election report access grants."));
        }
    }

    public override async Task<GetElectionCeremonyActionViewResponse> GetElectionCeremonyActionView(GetElectionCeremonyActionViewRequest request, ServerCallContext context)
    {
        try
        {
            return await _queryApplicationService.GetElectionCeremonyActionViewAsync(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                request.ActorPublicAddress);
        }
        catch (FormatException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(GetElectionCeremonyActionView));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to fetch election ceremony action view."));
        }
    }

    public override async Task<GetElectionsByOwnerResponse> GetElectionsByOwner(GetElectionsByOwnerRequest request, ServerCallContext context)
    {
        try
        {
            return await _queryApplicationService.GetElectionsByOwnerAsync(request.OwnerPublicAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", nameof(GetElectionsByOwner));
            throw new RpcException(new Status(StatusCode.Internal, "Failed to fetch elections by owner."));
        }
    }

}
