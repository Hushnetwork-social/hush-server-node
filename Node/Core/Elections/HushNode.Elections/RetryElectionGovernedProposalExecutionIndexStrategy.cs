using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public class RetryElectionGovernedProposalExecutionIndexStrategy(
    IRetryElectionGovernedProposalExecutionTransactionHandler transactionHandler) : IIndexStrategy
{
    private readonly IRetryElectionGovernedProposalExecutionTransactionHandler _transactionHandler = transactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        RetryElectionGovernedProposalExecutionPayloadHandler.RetryElectionGovernedProposalExecutionPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await _transactionHandler.HandleRetryElectionGovernedProposalExecutionTransaction(
            (ValidatedTransaction<RetryElectionGovernedProposalExecutionPayload>)transaction);
}
