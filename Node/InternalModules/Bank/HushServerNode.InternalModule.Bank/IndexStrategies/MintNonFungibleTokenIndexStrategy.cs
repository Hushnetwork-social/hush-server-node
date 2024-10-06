using HushEcosystem.Model.Bank;
using HushEcosystem.Model.Blockchain;
using HushServerNode.Interfaces;

namespace HushServerNode.InternalModule.Bank.IndexStrategies;

public class MintNonFungibleTokenIndexStrategy : IIndexStrategy
{
    private readonly IBankService _bankService;

    public MintNonFungibleTokenIndexStrategy(IBankService bankService)
    {
        this._bankService = bankService;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction is MintNonFungibleToken)
        {
            return true;
        }

        return false;
    }

    public async Task Handle(VerifiedTransaction verifiedTransaction)
    {
        var mintNonFungibleTokenTransaction = verifiedTransaction.SpecificTransaction as MintNonFungibleToken;

        await this._bankService.MintNonFungibleToken(mintNonFungibleTokenTransaction);
    }
}
