using HushNode.Blockchain.Model.Transaction;
using HushNode.Blockchain.Storage.Model;

namespace HushNode.Blockchain.Workflows;

public interface IBlockAssemblerWorkflow
{
    Task AssembleGenesisBlockAsync(BlockchainState genesisBlockchainState);

    Task AssembleBlockAsync(
        BlockchainState blockchainState,
        IReadOnlyList<AbstractTransaction> transactions);
}
