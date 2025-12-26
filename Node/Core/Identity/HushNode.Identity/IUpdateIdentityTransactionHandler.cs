using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public interface IUpdateIdentityTransactionHandler
{
    Task HandleUpdateIdentityTransaction(ValidatedTransaction<UpdateIdentityPayload> transaction);
}
