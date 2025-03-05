using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction.States;
using HushNode.Interfaces;
using HushNode.InternalModules.Bank.Model;
using HushNode.InternalPayloads;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.InternalModules.Bank;

public class RewardTransactionHandler(
    IUnitOfWorkProvider<BankDbContext> unitOfWorkProvider,
    ILogger<RewardTransactionHandler> logger) : IRewardTransactionHandler
{
    private readonly IUnitOfWorkProvider<BankDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly ILogger<RewardTransactionHandler> _logger = logger;

    private SemaphoreSlim _handlerSemaphore = new(1, 1);

    public async Task HandleRewardTransactionAsync(ValidatedTransaction<RewardPayload> rewardTransaction)
    {
        await this._handlerSemaphore.WaitAsync();
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        var addressBalance = await readOnlyUnitOfWork
        .GetRepository<IBalanceRepository>()
        .GetCurrentTokenBalanceAsync(
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
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();
        var newAddressBalance = addressBalance with
        {
            Balance = rewardTransaction.Payload.Amount
        };

        await writableUnitOfWork
            .GetRepository<IBalanceRepository>()
            .CreateTokenBalanceAsync(newAddressBalance);
        await writableUnitOfWork.CommitAsync();

        this._logger.LogInformation($"Reward for {rewardTransaction.UserSignature.Signatory} granted: {rewardTransaction.Payload.Amount}");
    }

    private async Task UpdateTokenBalance(
        AddressBalance addressBalance, 
        ValidatedTransaction<RewardPayload> rewardTransaction)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();
        var newBalance = DecimalStringConverter.StringToDecimal(rewardTransaction.Payload.Amount) + 
                DecimalStringConverter.StringToDecimal(addressBalance.Balance);

        var newAddressBalance = addressBalance with
        {
            Balance = DecimalStringConverter.DecimalToString(newBalance, 9)
        };

        writableUnitOfWork
            .GetRepository<IBalanceRepository>()
            .UpdateTokenBalance(newAddressBalance);
        await writableUnitOfWork.CommitAsync();

        this._logger.LogInformation($"Reward for {rewardTransaction.UserSignature.Signatory} granted: {rewardTransaction.Payload.Amount}");
    }
}
