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

    public override Task<ElectionCommandResponse> AcceptElectionTrusteeInvitation(Proto.ResolveElectionTrusteeInvitationRequest request, ServerCallContext context) =>
        ResolveTrusteeInvitationAsync(request, _lifecycleService.AcceptTrusteeInvitationAsync, nameof(AcceptElectionTrusteeInvitation));

    public override Task<ElectionCommandResponse> RejectElectionTrusteeInvitation(Proto.ResolveElectionTrusteeInvitationRequest request, ServerCallContext context) =>
        ResolveTrusteeInvitationAsync(request, _lifecycleService.RejectTrusteeInvitationAsync, nameof(RejectElectionTrusteeInvitation));

    public override Task<ElectionCommandResponse> RevokeElectionTrusteeInvitation(Proto.ResolveElectionTrusteeInvitationRequest request, ServerCallContext context) =>
        throw new RpcException(new Status(
            StatusCode.FailedPrecondition,
            "Election writes must be submitted through HushBlockchain.SubmitSignedTransaction. HushElections is query-only for RevokeElectionTrusteeInvitation."));

    public override Task<ElectionCommandResponse> StartElectionCeremony(Proto.StartElectionCeremonyRequest request, ServerCallContext context) =>
        ExecuteCommandAsync(
            () => _lifecycleService.StartElectionCeremonyAsync(new Domain.StartElectionCeremonyRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                request.ActorPublicAddress,
                request.ProfileId)),
            nameof(StartElectionCeremony));

    public override Task<ElectionCommandResponse> RestartElectionCeremony(Proto.RestartElectionCeremonyRequest request, ServerCallContext context) =>
        ExecuteCommandAsync(
            () => _lifecycleService.RestartElectionCeremonyAsync(new Domain.RestartElectionCeremonyRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                request.ActorPublicAddress,
                request.ProfileId,
                request.RestartReason)),
            nameof(RestartElectionCeremony));

    public override Task<ElectionCommandResponse> PublishElectionCeremonyTransportKey(Proto.PublishElectionCeremonyTransportKeyRequest request, ServerCallContext context) =>
        ExecuteCommandAsync(
            () => _lifecycleService.PublishElectionCeremonyTransportKeyAsync(new Domain.PublishElectionCeremonyTransportKeyRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                ElectionGrpcMappings.ParseGuid(request.CeremonyVersionId, nameof(request.CeremonyVersionId)),
                request.ActorPublicAddress,
                request.TransportPublicKeyFingerprint)),
            nameof(PublishElectionCeremonyTransportKey));

    public override Task<ElectionCommandResponse> JoinElectionCeremony(Proto.JoinElectionCeremonyRequest request, ServerCallContext context) =>
        ExecuteCommandAsync(
            () => _lifecycleService.JoinElectionCeremonyAsync(new Domain.JoinElectionCeremonyRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                ElectionGrpcMappings.ParseGuid(request.CeremonyVersionId, nameof(request.CeremonyVersionId)),
                request.ActorPublicAddress)),
            nameof(JoinElectionCeremony));

    public override Task<ElectionCommandResponse> RecordElectionCeremonySelfTestSuccess(Proto.RecordElectionCeremonySelfTestRequest request, ServerCallContext context) =>
        ExecuteCommandAsync(
            () => _lifecycleService.RecordElectionCeremonySelfTestSuccessAsync(new Domain.RecordElectionCeremonySelfTestRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                ElectionGrpcMappings.ParseGuid(request.CeremonyVersionId, nameof(request.CeremonyVersionId)),
                request.ActorPublicAddress)),
            nameof(RecordElectionCeremonySelfTestSuccess));

    public override Task<ElectionCommandResponse> SubmitElectionCeremonyMaterial(Proto.SubmitElectionCeremonyMaterialRequest request, ServerCallContext context) =>
        ExecuteCommandAsync(
            () => _lifecycleService.SubmitElectionCeremonyMaterialAsync(new Domain.SubmitElectionCeremonyMaterialRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                ElectionGrpcMappings.ParseGuid(request.CeremonyVersionId, nameof(request.CeremonyVersionId)),
                request.ActorPublicAddress,
                NormalizeOptionalString(request.RecipientTrusteeUserAddress),
                request.MessageType,
                request.PayloadVersion,
                request.EncryptedPayload.ToByteArray(),
                request.PayloadFingerprint)),
            nameof(SubmitElectionCeremonyMaterial));

    public override Task<ElectionCommandResponse> RecordElectionCeremonyValidationFailure(Proto.RecordElectionCeremonyValidationFailureRequest request, ServerCallContext context) =>
        ExecuteCommandAsync(
            () => _lifecycleService.RecordElectionCeremonyValidationFailureAsync(new Domain.RecordElectionCeremonyValidationFailureRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                ElectionGrpcMappings.ParseGuid(request.CeremonyVersionId, nameof(request.CeremonyVersionId)),
                request.ActorPublicAddress,
                request.TrusteeUserAddress,
                request.ValidationFailureReason,
                NormalizeOptionalString(request.EvidenceReference))),
            nameof(RecordElectionCeremonyValidationFailure));

    public override Task<ElectionCommandResponse> CompleteElectionCeremonyTrustee(Proto.CompleteElectionCeremonyTrusteeRequest request, ServerCallContext context) =>
        ExecuteCommandAsync(
            () => _lifecycleService.CompleteElectionCeremonyTrusteeAsync(new Domain.CompleteElectionCeremonyTrusteeRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                ElectionGrpcMappings.ParseGuid(request.CeremonyVersionId, nameof(request.CeremonyVersionId)),
                request.ActorPublicAddress,
                request.TrusteeUserAddress,
                request.ShareVersion,
                NormalizeOptionalString(request.TallyPublicKeyFingerprint))),
            nameof(CompleteElectionCeremonyTrustee));

    public override Task<ElectionCommandResponse> RecordElectionCeremonyShareExport(Proto.RecordElectionCeremonyShareExportRequest request, ServerCallContext context) =>
        ExecuteCommandAsync(
            () => _lifecycleService.RecordElectionCeremonyShareExportAsync(new Domain.RecordElectionCeremonyShareExportRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                ElectionGrpcMappings.ParseGuid(request.CeremonyVersionId, nameof(request.CeremonyVersionId)),
                request.ActorPublicAddress,
                request.ShareVersion)),
            nameof(RecordElectionCeremonyShareExport));

    public override Task<ElectionCommandResponse> RecordElectionCeremonyShareImport(Proto.RecordElectionCeremonyShareImportRequest request, ServerCallContext context) =>
        ExecuteCommandAsync(
            () => _lifecycleService.RecordElectionCeremonyShareImportAsync(new Domain.RecordElectionCeremonyShareImportRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                ElectionGrpcMappings.ParseGuid(request.CeremonyVersionId, nameof(request.CeremonyVersionId)),
                request.ActorPublicAddress,
                ElectionGrpcMappings.ParseElectionId(request.ImportedElectionId),
                ElectionGrpcMappings.ParseGuid(request.ImportedCeremonyVersionId, nameof(request.ImportedCeremonyVersionId)),
                request.ImportedTrusteeUserAddress,
                request.ImportedShareVersion)),
            nameof(RecordElectionCeremonyShareImport));

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

    private Task<ElectionCommandResponse> ResolveTrusteeInvitationAsync(
        Proto.ResolveElectionTrusteeInvitationRequest request,
        Func<Domain.ResolveElectionTrusteeInvitationRequest, Task<Domain.ElectionCommandResult>> operation,
        string operationName) =>
        ExecuteCommandAsync(
            () => operation(new Domain.ResolveElectionTrusteeInvitationRequest(
                ElectionGrpcMappings.ParseElectionId(request.ElectionId),
                ElectionGrpcMappings.ParseGuid(request.InvitationId, nameof(request.InvitationId)),
                request.ActorPublicAddress)),
            operationName);

    private async Task<ElectionCommandResponse> ExecuteCommandAsync(
        Func<Task<Domain.ElectionCommandResult>> operation,
        string operationName)
    {
        try
        {
            var result = await operation();
            return result.ToProto();
        }
        catch (FormatException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ElectionsGrpcService] Error in {Operation}", operationName);
            throw new RpcException(new Status(StatusCode.Internal, $"Failed to execute {operationName}."));
        }
    }

    private static string? NormalizeOptionalString(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
