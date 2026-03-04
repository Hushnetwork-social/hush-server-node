using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class CreateInnerCircleIndexStrategy(ICreateInnerCircleTransactionHandler createInnerCircleTransactionHandler) : IIndexStrategy
{
    private readonly ICreateInnerCircleTransactionHandler _createInnerCircleTransactionHandler = createInnerCircleTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        CreateInnerCirclePayloadHandler.CreateInnerCirclePayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._createInnerCircleTransactionHandler.HandleCreateInnerCircleTransactionAsync(
            (ValidatedTransaction<CreateInnerCirclePayload>)transaction);
}
