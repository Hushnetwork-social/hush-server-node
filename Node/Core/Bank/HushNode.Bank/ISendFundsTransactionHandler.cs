using HushShared.Bank.Model;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.Bank;

public interface ISendFundsTransactionHandler
{
    Task HandleSendFundsTransactionAsync(ValidatedTransaction<SendFundsPayload> sendFundsTransaction);
}
