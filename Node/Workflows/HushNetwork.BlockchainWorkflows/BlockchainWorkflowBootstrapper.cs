using System.Reactive.Subjects;
using Olimpo;

namespace HushNetwork.BlockchainWorkflows;

public class BlockchainWorkflowBootstrapper(IBlockchainWorkflow blockchainWorkflow) : IBootstrapper
{
    private readonly IBlockchainWorkflow _blockchainWorkflow = blockchainWorkflow;

    public Subject<bool> BootstrapFinished { get; } = new Subject<bool>();

    public int Priority { get; set; } = 10;

    public void Shutdown()
    {
    }

    public async Task Startup()
    {
        await this._blockchainWorkflow.InitializeBlockchainAsync();
        this.BootstrapFinished.OnNext(true);
    }
}
