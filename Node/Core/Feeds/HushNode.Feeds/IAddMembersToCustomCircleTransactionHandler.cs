using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface IAddMembersToCustomCircleTransactionHandler
{
    Task HandleAddMembersToCustomCircleTransactionAsync(ValidatedTransaction<AddMembersToCustomCirclePayload> transaction);
}
