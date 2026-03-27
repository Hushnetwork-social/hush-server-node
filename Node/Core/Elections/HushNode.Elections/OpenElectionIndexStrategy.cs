using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public class OpenElectionIndexStrategy(
    IOpenElectionTransactionHandler transactionHandler) : IIndexStrategy
{
    private readonly IOpenElectionTransactionHandler _transactionHandler = transactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        OpenElectionPayloadHandler.OpenElectionPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await _transactionHandler.HandleOpenElectionTransaction(
            (ValidatedTransaction<OpenElectionPayload>)transaction);
}
