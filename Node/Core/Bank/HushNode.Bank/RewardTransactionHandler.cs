using Microsoft.Extensions.Logging;
using HushNode.Bank.Model;
using HushNode.Bank.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Converters;

namespace HushNode.Bank;

public class RewardTransactionHandler(
    IBankStorageService bankStorageService,
    ILogger<RewardTransactionHandler> logger) : IRewardTransactionHandler
{
    private readonly IBankStorageService _bankStorageService = bankStorageService;
    private readonly ILogger<RewardTransactionHandler> _logger = logger;

    private readonly SemaphoreSlim _handlerSemaphore = new(1, 1);

    public async Task HandleRewardTransactionAsync(ValidatedTransaction<RewardPayload> rewardTransaction)
    {
        Console.WriteLine($"[RewardTransactionHandler] Processing reward for: {rewardTransaction.UserSignature.Signatory}");
        await _handlerSemaphore.WaitAsync();
        var addressBalance = await this._bankStorageService.RetrieveTokenBalanceForAddress(
            rewardTransaction.UserSignature.Signatory,
            rewardTransaction.Payload.Token);

        Func<AddressBalance, ValidatedTransaction<RewardPayload>, Task> handler =  addressBalance switch
        {
            AddressNoBalance => CreateTokenBalance,
            AddressBalance => UpdateTokenBalance
        };

        await handler.Invoke(addressBalance, rewardTransaction);

        this._handlerSemaphore.Release();
    }

    private async Task CreateTokenBalance(
        AddressBalance addressBalance, 
        ValidatedTransaction<RewardPayload> rewardTransaction)
    {
        var newAddressBalance = addressBalance with
        {
            Balance = rewardTransaction.Payload.Amount
        };

        await this._bankStorageService.CreateTokenBalanceForAddress(newAddressBalance);

        this._logger.LogInformation($"Reward for {rewardTransaction.UserSignature.Signatory} granted: {rewardTransaction.Payload.Amount}");
    }

    private async Task UpdateTokenBalance(
        AddressBalance addressBalance, 
        ValidatedTransaction<RewardPayload> rewardTransaction)
    {
        var newBalance = 
            DecimalStringConverter.StringToDecimal(rewardTransaction.Payload.Amount) + 
            DecimalStringConverter.StringToDecimal(addressBalance.Balance);

        var updatedAddressBalance = addressBalance with
        {
            Balance = DecimalStringConverter.DecimalToString(newBalance, 9)
        };

        await this._bankStorageService.UpdateTokenBalanceForAddress(updatedAddressBalance);

        this._logger.LogInformation($"Reward for {rewardTransaction.UserSignature.Signatory} granted: {rewardTransaction.Payload.Amount}");
    }
}
