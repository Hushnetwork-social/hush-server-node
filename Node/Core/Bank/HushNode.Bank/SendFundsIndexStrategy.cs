using HushNode.Indexing.Interfaces;
using HushShared.Bank.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.Bank;

public class SendFundsIndexStrategy(
    ISendFundsTransactionHandler sendFundsTransactionHandler) 
    : IIndexStrategy
{
    private readonly ISendFundsTransactionHandler _sendFundsTransactionHandler = sendFundsTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) => 
        SendFundsPayloadHandler.SendFundsPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction)
    {
        await this._sendFundsTransactionHandler
            .HandleSendFundsTransactionAsync((ValidatedTransaction<SendFundsPayload>)transaction);
    }
}
