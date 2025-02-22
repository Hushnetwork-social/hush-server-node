using System.Reactive.Linq;
using HushNode.Blockchain.Events;
using HushNode.Blockchain.Workflows;
using Olimpo;

namespace HushNode.Blockchain.Services;

public class BlockProductionSchedulerService : 
    IBlockProductionSchedulerService,
    IHandle<BlockchainInitializedEvent>
{
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow;
    private readonly IEventAggregator eventAggregator;
    private readonly IObservable<long> _blockGeneratorLoop;

    public BlockProductionSchedulerService(
        IBlockAssemblerWorkflow blockAssemblerWorkflow,
        IEventAggregator eventAggregator)
    {
        this._blockAssemblerWorkflow = blockAssemblerWorkflow;
        this.eventAggregator = eventAggregator;

        this.eventAggregator.Subscribe(this);

        this._blockGeneratorLoop = Observable.Interval(TimeSpan.FromSeconds(3));
    }

    public void Handle(BlockchainInitializedEvent message)
    {
        this._blockGeneratorLoop.Subscribe(x =>
        {
            Console.WriteLine("Block generated: {0}", DateTime.UtcNow);
        });
    }
}
