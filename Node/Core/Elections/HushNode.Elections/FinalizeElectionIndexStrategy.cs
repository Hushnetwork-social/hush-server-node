using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public class FinalizeElectionIndexStrategy(
    IFinalizeElectionTransactionHandler transactionHandler) : IIndexStrategy
{
    private readonly IFinalizeElectionTransactionHandler _transactionHandler = transactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        FinalizeElectionPayloadHandler.FinalizeElectionPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await _transactionHandler.HandleFinalizeElectionTransaction(
            (ValidatedTransaction<FinalizeElectionPayload>)transaction);
}
