using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IOpenElectionTransactionHandler
{
    Task HandleOpenElectionTransaction(ValidatedTransaction<OpenElectionPayload> transaction);
}
