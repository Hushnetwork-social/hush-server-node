using Olimpo.EntityFramework.Persistency;
using HushNode.Bank.Model;

namespace HushNode.Bank.Storage;

public class BankStorageService(
    IUnitOfWorkProvider<BankDbContext> unitOfWorkProvider) 
    : IBankStorageService
{
    private readonly IUnitOfWorkProvider<BankDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public async Task<AddressBalance> RetrieveTokenBalanceForAddress(string publicSignAddress, string token)
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider
            .CreateReadOnly();
            
        return await readOnlyUnitOfWork
            .GetRepository<IBalanceRepository>()
            .GetCurrentTokenBalanceAsync(
                publicSignAddress,
                token);
    }

    public async Task CreateTokenBalanceForAddress(AddressBalance addressBalance)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider
            .CreateWritable();

        await writableUnitOfWork
            .GetRepository<IBalanceRepository>()
            .CreateTokenBalanceAsync(addressBalance);

        await writableUnitOfWork
            .CommitAsync();
    }

    public async Task UpdateTokenBalanceForAddress(AddressBalance addressBalance)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider
            .CreateWritable();

        writableUnitOfWork
            .GetRepository<IBalanceRepository>()
            .UpdateTokenBalance(addressBalance);

        await writableUnitOfWork
            .CommitAsync();
    }

    public async Task PersistNewTokenBalances(
        string originPublicSigningAddress, 
        string newOriginBalance, 
        string targetPublicSigningAddress, 
        string newTargetBalance, 
        string token)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider
            .CreateWritable();

        var originTokenBalance = new AddressBalance(
            originPublicSigningAddress,
            token,
            newOriginBalance);

        var newTargetTokenBalance = new AddressBalance(
            targetPublicSigningAddress, 
            token, 
            newTargetBalance);

        writableUnitOfWork
            .GetRepository<IBalanceRepository>()
            .UpdateTokenBalance(originTokenBalance);

        writableUnitOfWork
            .GetRepository<IBalanceRepository>()
            .UpdateTokenBalance(newTargetTokenBalance);

        await writableUnitOfWork
            .CommitAsync();
    }
}
