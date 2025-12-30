using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface IUpdateGroupFeedDescriptionTransactionHandler
{
    Task HandleUpdateGroupFeedDescriptionTransactionAsync(ValidatedTransaction<UpdateGroupFeedDescriptionPayload> updateDescriptionTransaction);
}
