using HushNode.Blockchain.Persistency.Abstractions.Models;

namespace HushNode.Blockchain.Workflows;

public interface IBlockAssemblerWorkflow
{
    Task AssembleGenesisBlockAsync(BlockchainState genesisBlockchainState);
}
