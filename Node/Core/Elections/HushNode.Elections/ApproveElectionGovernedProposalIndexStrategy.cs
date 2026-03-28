using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public class ApproveElectionGovernedProposalIndexStrategy(
    IApproveElectionGovernedProposalTransactionHandler transactionHandler) : IIndexStrategy
{
    private readonly IApproveElectionGovernedProposalTransactionHandler _transactionHandler = transactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        ApproveElectionGovernedProposalPayloadHandler.ApproveElectionGovernedProposalPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await _transactionHandler.HandleApproveElectionGovernedProposalTransaction(
            (ValidatedTransaction<ApproveElectionGovernedProposalPayload>)transaction);
}
