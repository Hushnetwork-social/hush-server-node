using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Olimpo;
using HushNode.Blockchain.Storage;
using HushNode.Blockchain.Workflows;
using HushNode.Events;
using HushNode.MemPool;
using HushNode.Caching;

namespace HushNode.Blockchain.Services;

public class BlockProductionSchedulerService : 
    IBlockProductionSchedulerService,
    IHandle<BlockchainInitializedEvent>,
    IHandle<BlockCreatedEvent>
{
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow;
    private readonly IMemPoolService _memPool;
    private readonly IBlockchainStorageService _blockchainStorageService;
    private readonly IBlockchainCache _blockchainCache;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<BlockProductionSchedulerService> _logger;
    private readonly IObservable<long> _blockGeneratorLoop;
    private bool _canSchedule = true;

    public BlockProductionSchedulerService(
        IBlockAssemblerWorkflow blockAssemblerWorkflow,
        IMemPoolService memPool,
        IBlockchainStorageService blockchainStorageService,
        IBlockchainCache blockchainCache,
        IEventAggregator eventAggregator,
        ILogger<BlockProductionSchedulerService> logger)
    {
        this._blockAssemblerWorkflow = blockAssemblerWorkflow;
        this._memPool = memPool;
        this._blockchainStorageService = blockchainStorageService;
        this._blockchainCache = blockchainCache;
        this._eventAggregator = eventAggregator;
        this._logger = logger;

        this._eventAggregator.Subscribe(this);

        this._blockGeneratorLoop = Observable.Interval(TimeSpan.FromSeconds(3));
    }

    public void Handle(BlockchainInitializedEvent message)
    {
        // TODO [AboimPinto]: There are several business logic to generate a Block. 
        //                    If this is a PrivateNetwork, there is no aditional logic.
        //                    If this is attached to the main network, should follow the Block Producer rotation.

        this._blockGeneratorLoop.Subscribe(async x =>
        {
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

                await this._blockAssemblerWorkflow.AssembleBlockAsync(pendingTransactions);
            }
        });
    }

    public void Handle(BlockCreatedEvent message)
    {
        this._canSchedule = true;
    }
}

