using HushNode.Blockchain.Storage.Model;
using HushShared.Blockchain.TransactionModel;

namespace HushNode.Blockchain.Workflows;

public interface IBlockAssemblerWorkflow
{
    Task AssembleGenesisBlockAsync(BlockchainState genesisBlockchainState);

    Task AssembleBlockAsync(
        BlockchainState blockchainState,
        IEnumerable<AbstractTransaction> transactions);
}
