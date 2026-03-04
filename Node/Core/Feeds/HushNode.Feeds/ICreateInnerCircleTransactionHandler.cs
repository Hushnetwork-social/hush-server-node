using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface ICreateInnerCircleTransactionHandler
{
    Task HandleCreateInnerCircleTransactionAsync(ValidatedTransaction<CreateInnerCirclePayload> transaction);
}
