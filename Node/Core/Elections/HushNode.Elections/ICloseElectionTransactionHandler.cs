using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface ICloseElectionTransactionHandler
{
    Task HandleCloseElectionTransaction(ValidatedTransaction<CloseElectionPayload> transaction);
}
