// using HushEcosystem.Model.Blockchain;
// using HushServerNode.Interfaces;

// namespace HushServerNode.InternalModule.Blockchain.IndexStrategies;

// public class RewardTransactionIndexStrategy : IIndexStrategy
// {
//     private readonly IBlockchainService _blockchainService;

//     public RewardTransactionIndexStrategy(IBlockchainService blockchainService)
//     {
//         this._blockchainService = blockchainService;
//     }

//     public bool CanHandle(VerifiedTransaction verifiedTransaction)
//     {
//         if (verifiedTransaction.SpecificTransaction is IValueableTransaction)
//         {
//             return true;
//         }

//         return false;
//     }

//     public async Task Handle(VerifiedTransaction verifiedTransaction)
//     {
//         var valueableTransaction = verifiedTransaction.SpecificTransaction as IValueableTransaction;

//         await this._blockchainCache.UpdateBalanceAsync(
//             verifiedTransaction.SpecificTransaction.Issuer,
//             valueableTransaction.Value);
//     }
// }
