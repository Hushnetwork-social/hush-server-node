using HushNode.Credentials;
using HushNode.Elections.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class InviteElectionTrusteeContentHandler(
    ICredentialsProvider credentialProvider,
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public bool CanValidate(Guid transactionKind) =>
        InviteElectionTrusteePayloadHandler.InviteElectionTrusteePayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<InviteElectionTrusteePayload>;
        if (signedTransaction is null)
        {
            return null;
        }

        var signatory = signedTransaction.UserSignature?.Signatory;
        if (!string.Equals(signatory, signedTransaction.Payload.ActorPublicAddress, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(signatory)
            || string.IsNullOrWhiteSpace(signedTransaction.Payload.TrusteeUserAddress))
        {
            return null;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = repository.GetElectionAsync(signedTransaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (election is null
            || election.LifecycleState != ElectionLifecycleState.Draft
            || election.GovernanceMode != ElectionGovernanceMode.TrusteeThreshold
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

        var invitations = repository.GetTrusteeInvitationsAsync(signedTransaction.Payload.ElectionId).GetAwaiter().GetResult();
        var duplicateActiveInvitation = invitations.Any(x =>
            string.Equals(x.TrusteeUserAddress, signedTransaction.Payload.TrusteeUserAddress, StringComparison.OrdinalIgnoreCase)
            && (x.Status == ElectionTrusteeInvitationStatus.Pending || x.Status == ElectionTrusteeInvitationStatus.Accepted));
        if (duplicateActiveInvitation || invitations.Any(x => x.Id == signedTransaction.Payload.InvitationId))
        {
            return null;
        }

        var blockProducerCredentials = _credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
