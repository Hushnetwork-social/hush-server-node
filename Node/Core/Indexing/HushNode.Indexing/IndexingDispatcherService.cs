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
    private readonly IEnumerable<IBlockIndexCompletionRecorder> _completionRecorders;

    public IndexingDispatcherService(
        IEnumerable<IIndexStrategy> indexStrategies,
        IEventAggregator eventAggregator,
        Action? onBlockIndexCompleted = null,
        IEnumerable<IBlockContextIndexStrategy>? blockContextStrategies = null,
        IEnumerable<IBlockIndexCompletionRecorder>? completionRecorders = null)
    {
        this._indexStrategies = indexStrategies;
        this._blockContextStrategies = blockContextStrategies ?? [];
        this._eventAggregator = eventAggregator;
        this._onBlockIndexCompleted = onBlockIndexCompleted;
        this._completionRecorders = completionRecorders ?? [];

        this._eventAggregator.Subscribe(this);
    }

    public async Task HandleAsync(BlockCreatedEvent message)
    {
        Console.WriteLine($"[E2E] IndexingDispatcherService: Processing block {message.Block.BlockIndex.Value} with {message.Block.Transactions.Count()} transaction(s)");

        // Process transactions in block order to avoid write races
        // on shared domain aggregates (e.g., multiple group/inner-circle
        // membership mutations in the same block).
        var transactionPosition = 0;
        foreach (var transaction in message.Block.Transactions)
        {
            var blockContext = new BlockIndexContext(message.Block.BlockIndex.Value,
                message.Block.CreationTimeStamp.Value, message.Block.BlockId.Value, transactionPosition++);
            using var execution = BlockTransactionExecutionScope.Enter(new(transaction.TransactionId.Value, blockContext));
            // FEAT-015 D6: block-context strategies receive authoritative block index + consensus time.
            var blockContextStrategyTasks = this._blockContextStrategies
                .Where(strategy => strategy.CanHandle(transaction))
                .Select(strategy => strategy.HandleAsync(transaction, blockContext));

            await Task.WhenAll(blockContextStrategyTasks);

            var strategyTasks = this._indexStrategies
                .Where(strategy => strategy.CanHandle(transaction))
                .Select(strategy => strategy.HandleAsync(transaction));

            await Task.WhenAll(strategyTasks);
        }

        // Persist completion before notifying schedulers. The chain head was written before
        // dispatch and cannot itself prove that indexing finished.
        foreach (var recorder in _completionRecorders)
            await recorder.RecordAsync(message.Block.BlockIndex.Value, message.Block.BlockId.Value, message.Block.Hash,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(message.Block.ToJson()))));

        // Signal that all indexing for this block is complete
        Console.WriteLine($"[E2E] IndexingDispatcherService: Publishing BlockIndexCompletedEvent for block {message.Block.BlockIndex.Value}");
        await this._eventAggregator.PublishAsync(new BlockIndexCompletedEvent(message.Block.BlockIndex));
        this._onBlockIndexCompleted?.Invoke();
    }
}
