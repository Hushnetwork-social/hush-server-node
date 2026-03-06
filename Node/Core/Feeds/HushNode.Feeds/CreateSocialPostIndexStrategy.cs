using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class CreateSocialPostIndexStrategy(ICreateSocialPostTransactionHandler createSocialPostTransactionHandler) : IIndexStrategy
{
    private readonly ICreateSocialPostTransactionHandler _createSocialPostTransactionHandler = createSocialPostTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        CreateSocialPostPayloadHandler.CreateSocialPostPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._createSocialPostTransactionHandler.HandleCreateSocialPostTransactionAsync(
            (ValidatedTransaction<CreateSocialPostPayload>)transaction);
}
