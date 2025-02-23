using System.Reactive.Linq;
using HushNode.Blockchain.Events;
using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Workflows;
using HushNode.MemPool;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Blockchain.Services;

public class BlockProductionSchedulerService : 
    IBlockProductionSchedulerService,
    IHandle<BlockchainInitializedEvent>
{
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow;
    private readonly IMemPoolService _memPool;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<BlockProductionSchedulerService> _logger;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IObservable<long> _blockGeneratorLoop;

    public BlockProductionSchedulerService(
        IBlockAssemblerWorkflow blockAssemblerWorkflow,
        IMemPoolService memPool,
        IUnitOfWorkFactory unitOfWorkFactory,
        IEventAggregator eventAggregator,
        ILogger<BlockProductionSchedulerService> logger)
    {
        this._blockAssemblerWorkflow = blockAssemblerWorkflow;
        this._memPool = memPool;
        this._eventAggregator = eventAggregator;
        this._unitOfWorkFactory = unitOfWorkFactory;
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
            this._logger.LogInformation("Generating a block...");

            var readonlyUnitOfWork = this._unitOfWorkFactory.Create();
            var blockchainState = await readonlyUnitOfWork.BlockStateRepository.GetCurrentStateAsync();

            var pendingTransactions = await this._memPool.GetPendingValidatedTransactionsAsync();

            await this._blockAssemblerWorkflow.AsembleBlockAsync(blockchainState, pendingTransactions);
        });
    }
}

