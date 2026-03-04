using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class CreateCustomCircleIndexStrategy(ICreateCustomCircleTransactionHandler createCustomCircleTransactionHandler) : IIndexStrategy
{
    private readonly ICreateCustomCircleTransactionHandler _createCustomCircleTransactionHandler = createCustomCircleTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        CreateCustomCirclePayloadHandler.CreateCustomCirclePayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._createCustomCircleTransactionHandler.HandleCreateCustomCircleTransactionAsync(
            (ValidatedTransaction<CreateCustomCirclePayload>)transaction);
}
