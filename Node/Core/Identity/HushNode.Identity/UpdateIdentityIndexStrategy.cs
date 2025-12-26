using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class UpdateIdentityIndexStrategy(IUpdateIdentityTransactionHandler updateIdentityTransactionHandler) : IIndexStrategy
{
    private readonly IUpdateIdentityTransactionHandler _updateIdentityTransactionHandler = updateIdentityTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        UpdateIdentityPayloadHandler.UpdateIdentityPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._updateIdentityTransactionHandler.HandleUpdateIdentityTransaction((ValidatedTransaction<UpdateIdentityPayload>)transaction);
}
