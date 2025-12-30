using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class PromoteToAdminIndexStrategy(IPromoteToAdminTransactionHandler promoteToAdminTransactionHandler) : IIndexStrategy
{
    private readonly IPromoteToAdminTransactionHandler _promoteToAdminTransactionHandler = promoteToAdminTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        PromoteToAdminPayloadHandler.PromoteToAdminPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._promoteToAdminTransactionHandler.HandlePromoteToAdminTransactionAsync(
            (ValidatedTransaction<PromoteToAdminPayload>)transaction);
}
