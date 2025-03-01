using HushNode.Events;
using Olimpo;

namespace HushNode.Indexing;

public class IndexingDispatcherService :
    IIndexingDispatcherService,
    IHandleAsync<BlockCreatedEvent>
{
    private readonly IEnumerable<IIndexStrategy> _indexStrategies;
    private readonly IEventAggregator _eventAggregator;

    public IndexingDispatcherService(
        IEnumerable<IIndexStrategy> indexStrategies,
        IEventAggregator eventAggregator)
    {
        this._indexStrategies = indexStrategies;
        this._eventAggregator = eventAggregator;

        this._eventAggregator.Subscribe(this);
    }

    public async Task HandleAsync(BlockCreatedEvent message)
    {
        var processingTasks = message.Block.Transactions
            .Select(async transaction => 
            {
                var strategyTasks = _indexStrategies
                    .Where(strategy => strategy.CanHandle(transaction))
                    .Select(strategy => strategy.HandleAsync(transaction));
                    
                await Task.WhenAll(strategyTasks);
            });

        await Task.WhenAll(processingTasks);
    }
}