using HushNode.Events;
using HushNode.Indexing.Interfaces;
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
        Console.WriteLine($"[E2E] IndexingDispatcherService: Processing block {message.Block.BlockIndex.Value} with {message.Block.Transactions.Count()} transaction(s)");

        var processingTasks = message.Block.Transactions
            .Select(async transaction =>
            {
                var strategyTasks = this._indexStrategies
                    .Where(strategy => strategy.CanHandle(transaction))
                    .Select(strategy => strategy.HandleAsync(transaction));

                await Task.WhenAll(strategyTasks);
            });

        await Task.WhenAll(processingTasks);

        // Signal that all indexing for this block is complete
        Console.WriteLine($"[E2E] IndexingDispatcherService: Publishing BlockIndexCompletedEvent for block {message.Block.BlockIndex.Value}");
        await this._eventAggregator.PublishAsync(new BlockIndexCompletedEvent(message.Block.BlockIndex));
    }
}