using System.Reactive.Linq;
using HushEcosystem.Model.Blockchain;
using HushServerNode.InternalModule.Blockchain.Builders;
using HushServerNode.InternalModule.Blockchain.Events;
using HushServerNode.InternalModule.Blockchain.Factories;
using HushServerNode.InternalModule.MemPool;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushServerNode.InternalModule.Blockchain;

public class BlockGeneratorService :
    IBlockGeneratorService,
    IHandle<BlockchainInitializedEvent>
{
    private readonly IBlockBuilder _blockBuilder;
    private readonly IBlockCreatedEventFactory _blockCreatedEventFactory;
    private readonly IMemPoolService _memPoolService;
    private readonly IBlockchainStatus _blockchainStatus;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<BlockGeneratorService> _logger;
    private IObservable<long> _blockGeneratorLoop;

    public BlockGeneratorService(
        IBlockBuilder blockBuilder,
        IBlockCreatedEventFactory blockCreatedEventFactory,
        IMemPoolService memPoolService,
        IBlockchainStatus blockchainStatus,
        IEventAggregator eventAggregator,
        ILogger<BlockGeneratorService> logger)
    {
        this._blockBuilder = blockBuilder;
        this._blockCreatedEventFactory = blockCreatedEventFactory;
        this._memPoolService = memPoolService;
        this._blockchainStatus = blockchainStatus;
        this._eventAggregator = eventAggregator;
        this._logger = logger;

        this._eventAggregator.Subscribe(this);

        this._blockGeneratorLoop = Observable.Interval(TimeSpan.FromSeconds(3));
    }

    public void Handle(BlockchainInitializedEvent message)
    {
        this._blockGeneratorLoop.Subscribe(async x => 
        {
            var transactions = this._memPoolService.GetNextBlockTransactionsCandidate();

            this._blockchainStatus.UpdateBlockchainStatus(
                this._blockchainStatus.BlockIndex + 1,
                this._blockchainStatus.BlockId,
                this._blockchainStatus.NextBlockId,
                Guid.NewGuid().ToString());


            // TODO [AboimPinto] Add the transactions to the block
            var block = this._blockBuilder
                .WithBlockIndex(this._blockchainStatus.BlockIndex)
                .WithBlockId(this._blockchainStatus.BlockId)
                .WithPreviousBlockId(this._blockchainStatus.PreviousBlockId)
                .WithNextBlockId(this._blockchainStatus.NextBlockId)
                .WithRewardBeneficiary(
                    this._blockchainStatus.PublicSigningAddress,
                    this._blockchainStatus.PrivateSigningKey,
                    this._blockchainStatus.BlockIndex)
                .WithTransactions(transactions)
                .Build();

            await this._eventAggregator.PublishAsync(this._blockCreatedEventFactory.GetInstance(block, this._blockchainStatus.ToBlockchainState()));
        });
    }
}
