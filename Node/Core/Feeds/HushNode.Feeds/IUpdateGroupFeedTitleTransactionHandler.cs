using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface IUpdateGroupFeedTitleTransactionHandler
{
    Task HandleUpdateGroupFeedTitleTransactionAsync(ValidatedTransaction<UpdateGroupFeedTitlePayload> updateTitleTransaction);
}
