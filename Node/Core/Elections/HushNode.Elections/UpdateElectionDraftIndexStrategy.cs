using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public class UpdateElectionDraftIndexStrategy(
    IUpdateElectionDraftTransactionHandler transactionHandler) : IIndexStrategy
{
    private readonly IUpdateElectionDraftTransactionHandler _transactionHandler = transactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        UpdateElectionDraftPayloadHandler.UpdateElectionDraftPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await _transactionHandler.HandleUpdateElectionDraftTransaction(
            (ValidatedTransaction<UpdateElectionDraftPayload>)transaction);
}
