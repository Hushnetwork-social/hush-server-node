namespace HushNetwork.BlockchainWorkflows;

public class BlockchainWorkflow : IBlockchainWorkflow
{
    public Task InitializeBlockchainAsync()
    {
        return Task.CompletedTask;
    }
}
