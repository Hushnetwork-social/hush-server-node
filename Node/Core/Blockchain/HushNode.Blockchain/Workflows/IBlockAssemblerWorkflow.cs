using HushShared.Blockchain.TransactionModel;

namespace HushNode.Blockchain.Workflows;

public interface IBlockAssemblerWorkflow
{
    Task AssembleGenesisBlockAsync();

    Task AssembleBlockAsync(IEnumerable<AbstractTransaction> transactions);
}
