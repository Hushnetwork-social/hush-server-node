using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IRetryElectionGovernedProposalExecutionTransactionHandler
{
    Task HandleRetryElectionGovernedProposalExecutionTransaction(ValidatedTransaction<RetryElectionGovernedProposalExecutionPayload> transaction);
}
