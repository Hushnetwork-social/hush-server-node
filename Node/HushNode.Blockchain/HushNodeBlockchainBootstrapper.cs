using System.Reactive.Subjects;
using HushNode.Blockchain.Services;
using HushNode.Blockchain.Workflows;
using Olimpo;

namespace HushNode.Blockchain;

public class HushNodeBlockchainBootstrapper(
    IBlockProductionSchedulerService blockProductionSchedulerService,
    IChainFoundationService chainFoundationService,
    IBlockAssemblerWorkflow blockAssemblerWorkflow) : IBootstrapper
{
    private readonly IBlockProductionSchedulerService _blockProductionSchedulerService = blockProductionSchedulerService;
    private readonly IChainFoundationService _chainFoundationService = chainFoundationService;
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow = blockAssemblerWorkflow;

    public Subject<bool> BootstrapFinished { get; } = new Subject<bool>();
    public int Priority { get; set; } = 10;

    public void Shutdown()
    {
    }

    public async Task Startup()
    {
        await this._chainFoundationService.InitializeChain();
        this.BootstrapFinished.OnNext(true);
    }
}
