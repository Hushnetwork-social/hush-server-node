using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public class StartElectionGovernedProposalIndexStrategy(
    IStartElectionGovernedProposalTransactionHandler transactionHandler) : IIndexStrategy
{
    private readonly IStartElectionGovernedProposalTransactionHandler _transactionHandler = transactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        StartElectionGovernedProposalPayloadHandler.StartElectionGovernedProposalPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await _transactionHandler.HandleStartElectionGovernedProposalTransaction(
            (ValidatedTransaction<StartElectionGovernedProposalPayload>)transaction);
}
