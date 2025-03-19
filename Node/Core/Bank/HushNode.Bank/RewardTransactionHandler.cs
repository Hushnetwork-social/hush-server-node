using HushNode.Bank.Model;
using HushNode.Bank.Storage;
using HushNode.Interfaces;
using HushShared.Blockchain.TransactionModel.States;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Bank;

public class RewardTransactionHandler(
    IUnitOfWorkProvider<BankDbContext> unitOfWorkProvider,
    ILogger<RewardTransactionHandler> logger) : IRewardTransactionHandler
{
    private readonly IUnitOfWorkProvider<BankDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly ILogger<RewardTransactionHandler> _logger = logger;

    private SemaphoreSlim _handlerSemaphore = new(1, 1);

    public async Task HandleRewardTransactionAsync(ValidatedTransaction<RewardPayload> rewardTransaction)
    {
        await _handlerSemaphore.WaitAsync();
        using var readOnlyUnitOfWork = _unitOfWorkProvider.CreateReadOnly();
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

        _handlerSemaphore.Release();
    }

    private async Task CreateTokenBalance(
        AddressBalance addressBalance, 
        ValidatedTransaction<RewardPayload> rewardTransaction)
    {
        using var writableUnitOfWork = _unitOfWorkProvider.CreateWritable();
        var newAddressBalance = addressBalance with
        {
            Balance = rewardTransaction.Payload.Amount
        };

        await writableUnitOfWork
            .GetRepository<IBalanceRepository>()
            .CreateTokenBalanceAsync(newAddressBalance);
        await writableUnitOfWork
            .CommitAsync();

        _logger.LogInformation($"Reward for {rewardTransaction.UserSignature.Signatory} granted: {rewardTransaction.Payload.Amount}");
    }

    private async Task UpdateTokenBalance(
        AddressBalance addressBalance, 
        ValidatedTransaction<RewardPayload> rewardTransaction)
    {
        using var writableUnitOfWork = _unitOfWorkProvider.CreateWritable();
        var newBalance = 
            DecimalStringConverter.StringToDecimal(rewardTransaction.Payload.Amount) + 
            DecimalStringConverter.StringToDecimal(addressBalance.Balance);

        var newAddressBalance = addressBalance with
        {
            Balance = DecimalStringConverter.DecimalToString(newBalance, 9)
        };

        writableUnitOfWork
            .GetRepository<IBalanceRepository>()
            .UpdateTokenBalance(newAddressBalance);
        await writableUnitOfWork.CommitAsync();

        _logger.LogInformation($"Reward for {rewardTransaction.UserSignature.Signatory} granted: {rewardTransaction.Payload.Amount}");
    }
}
