using HushNode.Events;
using HushNode.Indexing.Interfaces;
using Olimpo;

namespace HushNode.Indexing;

public class IndexingDispatcherService :
    IIndexingDispatcherService,
    IHandleAsync<BlockCreatedEvent>
{
    private readonly IEnumerable<IIndexStrategy> _indexStrategies;
    private readonly IEnumerable<IBlockContextIndexStrategy> _blockContextStrategies;
    private readonly IEventAggregator _eventAggregator;
    private readonly Action? _onBlockIndexCompleted;

    public IndexingDispatcherService(
        IEnumerable<IIndexStrategy> indexStrategies,
        IEventAggregator eventAggregator,
        Action? onBlockIndexCompleted = null,
        IEnumerable<IBlockContextIndexStrategy>? blockContextStrategies = null)
    {
        this._indexStrategies = indexStrategies;
        this._blockContextStrategies = blockContextStrategies ?? [];
        this._eventAggregator = eventAggregator;
        this._onBlockIndexCompleted = onBlockIndexCompleted;

        this._eventAggregator.Subscribe(this);
    }

    public async Task HandleAsync(BlockCreatedEvent message)
    {
        Console.WriteLine($"[E2E] IndexingDispatcherService: Processing block {message.Block.BlockIndex.Value} with {message.Block.Transactions.Count()} transaction(s)");

        var blockTimeUtc = message.Block.CreationTimeStamp.Value;

        // Process transactions in block order to avoid write races
        // on shared domain aggregates (e.g., multiple group/inner-circle
        // membership mutations in the same block).
        foreach (var transaction in message.Block.Transactions)
        {
            // FEAT-015 D6: block-context strategies receive the authoritative consensus time.
            var blockContextStrategyTasks = this._blockContextStrategies
                .Where(strategy => strategy.CanHandle(transaction))
                .Select(strategy => strategy.HandleAsync(transaction, blockTimeUtc));

            await Task.WhenAll(blockContextStrategyTasks);

            var strategyTasks = this._indexStrategies
                .Where(strategy => strategy.CanHandle(transaction))
                .Select(strategy => strategy.HandleAsync(transaction));

            await Task.WhenAll(strategyTasks);
        }

        // Signal that all indexing for this block is complete
        Console.WriteLine($"[E2E] IndexingDispatcherService: Publishing BlockIndexCompletedEvent for block {message.Block.BlockIndex.Value}");
        await this._eventAggregator.PublishAsync(new BlockIndexCompletedEvent(message.Block.BlockIndex));
        this._onBlockIndexCompleted?.Invoke();
    }
}
