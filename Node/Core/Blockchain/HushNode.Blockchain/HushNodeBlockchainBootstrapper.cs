using System.Reactive.Subjects;
using Olimpo;
using HushShared.Blockchain.TransactionModel;
using HushNode.Blockchain.Services;
using HushNode.Blockchain.Workflows;

namespace HushNode.Blockchain;

public class HushNodeBlockchainBootstrapper(
    IBlockProductionSchedulerService blockProductionSchedulerService,
    IChainFoundationService chainFoundationService,
    IBlockAssemblerWorkflow blockAssemblerWorkflow,
    TransactionDeserializerHandler transactionDeserializerHandler) : IBootstrapper
{
    private readonly IBlockProductionSchedulerService _blockProductionSchedulerService = blockProductionSchedulerService;
    private readonly IChainFoundationService _chainFoundationService = chainFoundationService;
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow = blockAssemblerWorkflow;
    private readonly TransactionDeserializerHandler transactionDeserializerHandler = transactionDeserializerHandler;

    public Subject<string> BootstrapFinished { get; } = new Subject<string>();
    public int Priority { get; set; } = 10;

    public void Shutdown()
    {
    }

    public async Task Startup()
    {
        await this._chainFoundationService.InitializeChain();
        this.BootstrapFinished.OnNext("Blockchain");
    }
}
