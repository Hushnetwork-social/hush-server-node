using HushNode.Credentials;
using HushNode.Elections.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class RetryElectionGovernedProposalExecutionContentHandler(
    ICredentialsProvider credentialProvider,
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public bool CanValidate(Guid transactionKind) =>
        RetryElectionGovernedProposalExecutionPayloadHandler.RetryElectionGovernedProposalExecutionPayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<RetryElectionGovernedProposalExecutionPayload>;
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
        var proposal = repository.GetGovernedProposalAsync(signedTransaction.Payload.ProposalId).GetAwaiter().GetResult();
        if (election is null
            || proposal is null
            || proposal.ElectionId != signedTransaction.Payload.ElectionId
            || !string.Equals(election.OwnerPublicAddress, signatory, StringComparison.Ordinal)
            || !proposal.CanRetry)
        {
            return null;
        }

        var approvals = repository.GetGovernedProposalApprovalsAsync(proposal.Id).GetAwaiter().GetResult();
        if (!election.RequiredApprovalCount.HasValue || approvals.Count < election.RequiredApprovalCount.Value)
        {
            return null;
        }

        var blockProducerCredentials = _credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
