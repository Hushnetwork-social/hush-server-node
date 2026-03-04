using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface ICreateCustomCircleTransactionHandler
{
    Task HandleCreateCustomCircleTransactionAsync(ValidatedTransaction<CreateCustomCirclePayload> transaction);
}
