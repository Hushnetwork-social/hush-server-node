using HushNode.Bank.Model;
using HushNode.Bank.Storage;
using HushShared.Bank.Model;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Converters;

namespace HushNode.Bank;

public class SendFundsTransactionHandler(IBankStorageService bankStorageService) : ISendFundsTransactionHandler
{
    private readonly IBankStorageService _bankStorageService = bankStorageService;

    public async Task HandleSendFundsTransactionAsync(ValidatedTransaction<SendFundsPayload> sendFundsTransaction)
    {
        var originBalance = await this._bankStorageService
            .RetrieveTokenBalanceForAddress(
                sendFundsTransaction.UserSignature.Signatory,
                sendFundsTransaction.Payload.Token);

        var targetBalance = await this._bankStorageService
            .RetrieveTokenBalanceForAddress(
                sendFundsTransaction.Payload.ToAddress,
                sendFundsTransaction.Payload.Token);

        if (targetBalance is AddressNoBalance)
        {
            // Create a balance for the PublicSigningAddress
            await this._bankStorageService.CreateTokenBalanceForAddress(targetBalance);
        }

        var newOriginBalance = 
            DecimalStringConverter.StringToDecimal(originBalance.Balance) -
            DecimalStringConverter.StringToDecimal(sendFundsTransaction.Payload.Amount); 

        var newTargetBalance = 
            DecimalStringConverter.StringToDecimal(targetBalance.Balance) +
            DecimalStringConverter.StringToDecimal(sendFundsTransaction.Payload.Amount); 

        await this._bankStorageService.PersistNewTokenBalances(
            sendFundsTransaction.UserSignature.Signatory, 
            DecimalStringConverter.DecimalToString(newOriginBalance),
            sendFundsTransaction.Payload.ToAddress,
            DecimalStringConverter.DecimalToString(newTargetBalance),
            sendFundsTransaction.Payload.Token);
    }
}
