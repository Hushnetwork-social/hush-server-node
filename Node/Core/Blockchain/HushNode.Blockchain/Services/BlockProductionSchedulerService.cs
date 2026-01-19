using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Olimpo;
using HushNode.Blockchain.Configuration;
using HushNode.Blockchain.Storage;
using HushNode.Blockchain.Workflows;
using HushNode.Events;
using HushNode.MemPool;
using HushNode.Caching;

namespace HushNode.Blockchain.Services;

public class BlockProductionSchedulerService :
    IBlockProductionSchedulerService,
    IHandle<BlockchainInitializedEvent>,
    IHandleAsync<BlockCreatedEvent>,
    IHandleAsync<BlockIndexCompletedEvent>,
    IHandle<TransactionReceivedEvent>
{
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow;
    private readonly IMemPoolService _memPool;
    private readonly IBlockchainStorageService _blockchainStorageService;
    private readonly IBlockchainCache _blockchainCache;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<BlockProductionSchedulerService> _logger;
    private readonly BlockchainSettings _blockchainSettings;
    private readonly IObservable<long> _blockGeneratorLoop;
    private readonly bool _isTestMode;
    private readonly Action? _onBlockFinalized;

    private bool _canSchedule = true;
    private int _consecutiveEmptyBlockCount = 0;
    private bool _isPausedForEmptyBlocks = false;

    /// <summary>
    /// Creates a BlockProductionSchedulerService for production use with default 3-second interval.
    /// </summary>
    public BlockProductionSchedulerService(
        IBlockAssemblerWorkflow blockAssemblerWorkflow,
        IMemPoolService memPool,
        IBlockchainStorageService blockchainStorageService,
        IBlockchainCache blockchainCache,
        IEventAggregator eventAggregator,
        IOptions<BlockchainSettings> blockchainSettings,
        ILogger<BlockProductionSchedulerService> logger)
        : this(blockAssemblerWorkflow, memPool, blockchainStorageService, blockchainCache,
               eventAggregator, blockchainSettings, logger, observableFactory: null)
    {
    }

    /// <summary>
    /// Creates a BlockProductionSchedulerService with optional observable factory injection for testing.
    /// When observableFactory is provided, the service runs in test mode which bypasses empty block pause logic.
    /// </summary>
    /// <param name="observableFactory">
    /// Optional factory function that returns the observable to trigger block production.
    /// If null, uses Observable.Interval(3 seconds) for production mode.
    /// If provided, enables test mode which bypasses empty block pause logic.
    /// </param>
    /// <param name="onBlockFinalized">
    /// Optional callback invoked when a block is finalized (persisted to database).
    /// Used by BlockProductionControl to signal completion of ProduceBlockAsync.
    /// </param>
    public BlockProductionSchedulerService(
        IBlockAssemblerWorkflow blockAssemblerWorkflow,
        IMemPoolService memPool,
        IBlockchainStorageService blockchainStorageService,
        IBlockchainCache blockchainCache,
        IEventAggregator eventAggregator,
        IOptions<BlockchainSettings> blockchainSettings,
        ILogger<BlockProductionSchedulerService> logger,
        Func<IObservable<long>>? observableFactory,
        Action? onBlockFinalized = null)
    {
        this._blockAssemblerWorkflow = blockAssemblerWorkflow;
        this._memPool = memPool;
        this._blockchainStorageService = blockchainStorageService;
        this._blockchainCache = blockchainCache;
        this._eventAggregator = eventAggregator;
        this._blockchainSettings = blockchainSettings.Value;
        this._logger = logger;
        this._isTestMode = observableFactory != null;
        this._onBlockFinalized = onBlockFinalized;

        this._eventAggregator.Subscribe(this);

        // Use provided factory or default to 3-second interval for production
        this._blockGeneratorLoop = observableFactory != null
            ? observableFactory()
            : Observable.Interval(TimeSpan.FromSeconds(3));

        this._logger.LogInformation(
            "BlockProductionSchedulerService created. TestMode={TestMode}, HasOnBlockFinalized={HasCallback}",
            this._isTestMode, this._onBlockFinalized != null);
    }

    public void Handle(BlockchainInitializedEvent message)
    {
        // TODO [AboimPinto]: There are several business logic to generate a Block.
        //                    If this is a PrivateNetwork, there is no aditional logic.
        //                    If this is attached to the main network, should follow the Block Producer rotation.

        this._logger.LogInformation("BlockchainInitializedEvent received. TestMode={TestMode}, OnBlockFinalized={HasCallback}",
            this._isTestMode, this._onBlockFinalized != null);

        this._blockGeneratorLoop.Subscribe(async x =>
        {
            // In test mode, bypass the empty block pause check entirely
            if (!this._isTestMode && this._isPausedForEmptyBlocks)
            {
                this._logger.LogInformation("Block production paused - waiting for transactions...");
                return;
            }

            if (!this._canSchedule)
            {
                this._logger.LogInformation("BlockAssembler is buzy. Cannot schedule a new block...");
            }
            else
            {
                this._canSchedule = false;
                this._logger.LogInformation("Generating a block...");

                if (!this._blockchainCache.BlockchainStateInDatabase)
                {
                    var blockchainState = await this._blockchainStorageService.RetrieveCurrentBlockchainStateAsync();

                    this._blockchainCache
                        .IsBlockchainStateInDatabase()
                        .SetBlockIndex(blockchainState.BlockIndex)
                        .SetPreviousBlockId(blockchainState.PreviousBlockId)
                        .SetCurrentBlockId(blockchainState.CurrentBlockId)
                        .SetNextBlockId(blockchainState.NextBlockId);
                }

                var pendingTransactions = this._memPool.GetPendingValidatedTransactionsAsync();
                var transactionsList = pendingTransactions.ToList();
                var userTransactionCount = transactionsList.Count;

                if (userTransactionCount == 0)
                {
                    this._consecutiveEmptyBlockCount++;
                    this._logger.LogInformation(
                        "Empty block (no user transactions) - {CurrentCount}/{MaxCount} before pause",
                        this._consecutiveEmptyBlockCount,
                        this._blockchainSettings.MaxEmptyBlocksBeforePause);

                    // In test mode, skip the pause logic entirely
                    if (!this._isTestMode && this._consecutiveEmptyBlockCount >= this._blockchainSettings.MaxEmptyBlocksBeforePause)
                    {
                        this._isPausedForEmptyBlocks = true;
                        this._logger.LogWarning(
                            "Block production paused after {Count} consecutive empty blocks. Waiting for transactions...",
                            this._consecutiveEmptyBlockCount);
                        this._canSchedule = true;
                        return;
                    }
                }
                else
                {
                    this._consecutiveEmptyBlockCount = 0;
                    this._logger.LogInformation(
                        "Block with {Count} user transaction(s)",
                        userTransactionCount);
                }

                await this._blockAssemblerWorkflow.AssembleBlockAsync(transactionsList);
            }
        });
    }

    public Task HandleAsync(BlockCreatedEvent message)
    {
        // Block has been created and saved - allow scheduling next block
        this._canSchedule = true;
        return Task.CompletedTask;
    }

    public Task HandleAsync(BlockIndexCompletedEvent message)
    {
        // All indexing for the block is complete - signal test can continue
        this._onBlockFinalized?.Invoke();
        return Task.CompletedTask;
    }

    public void Handle(TransactionReceivedEvent message)
    {
        if (this._isPausedForEmptyBlocks)
        {
            this._logger.LogInformation("Transaction received - resuming block production...");
            this._consecutiveEmptyBlockCount = 0;
            this._isPausedForEmptyBlocks = false;
        }
    }
}
