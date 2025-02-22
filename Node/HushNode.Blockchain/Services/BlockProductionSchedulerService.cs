using HushNode.Blockchain.Events;
using Olimpo;

namespace HushNode.Blockchain.Services;

public class BlockProductionSchedulerService : 
    IBlockProductionSchedulerService,
    IHandleAsync<BlockCreatedEvent>
{
    private readonly EventAggregator eventAggregator;

    public BlockProductionSchedulerService(EventAggregator eventAggregator)
    {
        this.eventAggregator = eventAggregator;

        this.eventAggregator.Subscribe(this);
    }

    public Task HandleAsync(BlockCreatedEvent message)
    {
        return Task.CompletedTask;
    }
}
