namespace HushNode.Blockchain.Services;

public class ChainFoundationService : IChainFoundationService
{
    public Task InitializeChain()
    {
        return Task.CompletedTask;
    }
}
