using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;
using HushNode.Blockchain.Persistency.EntityFramework;
using HushNode.Blockchain.Workflows;
using HushNode.Events;
using HushNode.MemPool;

namespace HushNode.Blockchain.Services;

public class BlockProductionSchedulerService : 
    IBlockProductionSchedulerService,
    IHandle<BlockchainInitializedEvent>,
    IHandle<BlockCreatedEvent>
{
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow;
    private readonly IMemPoolService _memPool;
    private readonly IEventAggregator _eventAggregator;
    private readonly IUnitOfWorkProvider<BlockchainDbContext> _unitOfWorkProvider;
    private readonly ILogger<BlockProductionSchedulerService> _logger;
    private readonly IObservable<long> _blockGeneratorLoop;
    private bool _canSchedule = true;

    public BlockProductionSchedulerService(
        IBlockAssemblerWorkflow blockAssemblerWorkflow,
        IMemPoolService memPool,
        IUnitOfWorkProvider<BlockchainDbContext> unitOfWorkProvider,
        IEventAggregator eventAggregator,
        ILogger<BlockProductionSchedulerService> logger)
    {
        this._blockAssemblerWorkflow = blockAssemblerWorkflow;
        this._memPool = memPool;
        this._unitOfWorkProvider = unitOfWorkProvider;
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

                var blockchainState = await this._unitOfWorkProvider.CreateReadOnly()
                    .GetRepository<IBlockchainStateRepository>()
                    .GetCurrentStateAsync();

                var pendingTransactions = await this._memPool.GetPendingValidatedTransactionsAsync();

                await this._blockAssemblerWorkflow.AssembleBlockAsync(blockchainState, pendingTransactions);
            }
        });
    }

    public void Handle(BlockCreatedEvent message)
    {
        this._canSchedule = true;
    }
}

