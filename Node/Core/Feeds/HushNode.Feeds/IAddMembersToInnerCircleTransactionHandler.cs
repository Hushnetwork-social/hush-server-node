using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface IAddMembersToInnerCircleTransactionHandler
{
    Task HandleAddMembersToInnerCircleTransactionAsync(ValidatedTransaction<AddMembersToInnerCirclePayload> transaction);
}
