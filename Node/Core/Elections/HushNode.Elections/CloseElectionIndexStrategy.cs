using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public class CloseElectionIndexStrategy(
    ICloseElectionTransactionHandler transactionHandler) : IIndexStrategy
{
    private readonly ICloseElectionTransactionHandler _transactionHandler = transactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        CloseElectionPayloadHandler.CloseElectionPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await _transactionHandler.HandleCloseElectionTransaction(
            (ValidatedTransaction<CloseElectionPayload>)transaction);
}
