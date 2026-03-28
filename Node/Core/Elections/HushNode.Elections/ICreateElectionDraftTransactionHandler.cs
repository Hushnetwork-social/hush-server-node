using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface ICreateElectionDraftTransactionHandler
{
    Task HandleCreateElectionDraftTransaction(ValidatedTransaction<CreateElectionDraftPayload> transaction);
}
