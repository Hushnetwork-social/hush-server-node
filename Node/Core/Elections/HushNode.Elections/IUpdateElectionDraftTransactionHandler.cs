using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IUpdateElectionDraftTransactionHandler
{
    Task HandleUpdateElectionDraftTransaction(ValidatedTransaction<UpdateElectionDraftPayload> transaction);
}
