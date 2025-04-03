using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public interface IFullIdentityTransactionHandler
{
    Task HandleFullIdentityTransaction(ValidatedTransaction<FullIdentityPayload> transaction);
}
