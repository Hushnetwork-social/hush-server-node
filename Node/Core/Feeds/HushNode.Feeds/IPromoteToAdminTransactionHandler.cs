using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface IPromoteToAdminTransactionHandler
{
    Task HandlePromoteToAdminTransactionAsync(ValidatedTransaction<PromoteToAdminPayload> promoteToAdminTransaction);
}
