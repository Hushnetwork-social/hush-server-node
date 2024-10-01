using HushEcosystem.Model.Bank;
using HushEcosystem.Model.Blockchain;
using HushServerNode.Interfaces;

namespace HushServerNode.InternalModule.Bank.IndexStrategies;

public class RewardIndexStrategy : IIndexStrategy
{
    private IBankService _bankService;

    public RewardIndexStrategy(IBankService bankService)
    {
        this._bankService = bankService;
    }


    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction is IValueableTransaction)
        {
            return true;
        }

        return false;
    }

    public async Task Handle(VerifiedTransaction verifiedTransaction)
    {
        var valueableTransaction = verifiedTransaction.SpecificTransaction as IValueableTransaction;

        await this._bankService.UpdateBalanceAsync(
            verifiedTransaction.SpecificTransaction.Issuer,
            valueableTransaction.Value);
    }
}
