using HushNode.Blockchain.Model;
using HushNode.Blockchain.Model.Transaction;

namespace HushNode.Blockchain.Workflows;

public interface IBlockAssemblerWorkflow
{
    Task AssembleGenesisBlockAsync(BlockchainState genesisBlockchainState);

    Task AssembleBlockAsync(
        BlockchainState blockchainState,
        IReadOnlyList<AbstractTransaction> transactions);
}
