using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface IUnblockMemberTransactionHandler
{
    Task HandleUnblockMemberTransactionAsync(ValidatedTransaction<UnblockMemberPayload> unblockMemberTransaction);
}
