using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class FullIdentityIndexStrategy(IFullIdentityTransactionHandler fullIdentityTransactionHandler) : IIndexStrategy
{
    private readonly IFullIdentityTransactionHandler _fullIdentityTransactionHandler = fullIdentityTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        FullIdentityPayloadHandler.FullIdentityPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) => 
        await this._fullIdentityTransactionHandler.HandleFullIdentityTransaction((ValidatedTransaction<FullIdentityPayload>)transaction);

}
