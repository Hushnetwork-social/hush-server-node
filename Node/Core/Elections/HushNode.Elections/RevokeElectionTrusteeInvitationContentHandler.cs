using HushNode.Credentials;
using HushNode.Elections.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class RevokeElectionTrusteeInvitationContentHandler(
    ICredentialsProvider credentialProvider,
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public bool CanValidate(Guid transactionKind) =>
        RevokeElectionTrusteeInvitationPayloadHandler.RevokeElectionTrusteeInvitationPayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<RevokeElectionTrusteeInvitationPayload>;
        if (signedTransaction is null)
        {
            return null;
        }

        var signatory = signedTransaction.UserSignature?.Signatory;
        if (!string.Equals(signatory, signedTransaction.Payload.ActorPublicAddress, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(signatory))
        {
            return null;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = repository.GetElectionAsync(signedTransaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (election is null
            || election.LifecycleState != ElectionLifecycleState.Draft
            || !string.Equals(election.OwnerPublicAddress, signatory, StringComparison.Ordinal))
        {
            return null;
        }

        var pendingProposal = repository.GetPendingGovernedProposalAsync(signedTransaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (pendingProposal is not null
            && pendingProposal.ActionType == ElectionGovernedActionType.Open
            && pendingProposal.ExecutionStatus == ElectionGovernedProposalExecutionStatus.WaitingForApprovals)
        {
            return null;
        }

        var invitation = repository.GetTrusteeInvitationAsync(signedTransaction.Payload.InvitationId).GetAwaiter().GetResult();
        if (invitation is null
            || invitation.ElectionId != signedTransaction.Payload.ElectionId
            || invitation.Status != ElectionTrusteeInvitationStatus.Pending)
        {
            return null;
        }

        var blockProducerCredentials = _credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
