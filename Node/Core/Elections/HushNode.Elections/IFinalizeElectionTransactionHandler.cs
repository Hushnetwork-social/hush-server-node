using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IFinalizeElectionTransactionHandler
{
    Task HandleFinalizeElectionTransaction(ValidatedTransaction<FinalizeElectionPayload> transaction);
}
