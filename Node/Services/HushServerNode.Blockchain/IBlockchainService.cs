using System.Collections.Generic;
using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;
using HushServerNode.CacheService;

namespace HushServerNode.Blockchain;

public interface IBlockchainService
{
    BlockchainState BlockchainState { get; set; }

    // Task InitializeBlockchainAsync();

    IEnumerable<VerifiedTransaction> ListTransactionsForAddress(string address, double lastHeightSynched);

    double GetBalanceForAddress(string address);

    HushUserProfile GetUserProfile(string publicAddress);

    // Task UpdateBlockchainState();

    // Task SaveBlock(Block block);
}
