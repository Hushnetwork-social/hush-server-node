using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IApproveElectionGovernedProposalTransactionHandler
{
    Task HandleApproveElectionGovernedProposalTransaction(ValidatedTransaction<ApproveElectionGovernedProposalPayload> transaction);
}
