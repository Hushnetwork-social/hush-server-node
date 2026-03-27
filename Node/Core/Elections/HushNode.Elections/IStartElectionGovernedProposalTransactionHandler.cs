using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IStartElectionGovernedProposalTransactionHandler
{
    Task HandleStartElectionGovernedProposalTransaction(ValidatedTransaction<StartElectionGovernedProposalPayload> transaction);
}
