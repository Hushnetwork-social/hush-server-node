using System.Reactive.Linq;
using HushEcosystem.Model.Blockchain;
using HushServerNode.Cache.Blockchain;
using HushServerNode.InternalModule.Blockchain.Builders;
using HushServerNode.InternalModule.Blockchain.Events;
using HushServerNode.InternalModule.Blockchain.Factories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushServerNode.InternalModule.Blockchain;

public class BlockGeneratorService :
    IBlockGeneratorService,
    IHandle<BlockchainInitializedEvent>
{
    private readonly IBlockBuilder _blockBuilder;
    private readonly IBlockCreatedEventFactory _blockCreatedEventFactory;
    private readonly IConfiguration _configuration;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<BlockGeneratorService> _logger;
    private IObservable<long> _blockGeneratorLoop;
    private BlockchainState _blockchainState;

    public BlockGeneratorService(
        IBlockBuilder blockBuilder,
        IBlockCreatedEventFactory blockCreatedEventFactory,
        IConfiguration configuration,
        IEventAggregator eventAggregator,
        ILogger<BlockGeneratorService> logger)
    {
        this._blockBuilder = blockBuilder;
        this._blockCreatedEventFactory = blockCreatedEventFactory;
        this._configuration = configuration;
        this._eventAggregator = eventAggregator;
        this._logger = logger;

        this._eventAggregator.Subscribe(this);

        this._blockGeneratorLoop = Observable.Interval(TimeSpan.FromSeconds(3));
    }

    public void Handle(BlockchainInitializedEvent message)
    {
        this._blockchainState = message.BlockchainState;

        this._blockGeneratorLoop.Subscribe(async x => 
        {
            // var transactions = this._memPoolService.GetNextBlockTransactionsCandidate();
            var transactions = new List<VerifiedTransaction>();

            this._blockchainState.LastBlockIndex ++; 
            this._blockchainState.CurrentPreviousBlockId = this._blockchainState.CurrentBlockId;
            this._blockchainState.CurrentBlockId = this._blockchainState.CurrentNextBlockId;
            this._blockchainState.CurrentNextBlockId = Guid.NewGuid().ToString();


            // TODO [AboimPinto] Add the transactions to the block
            var block = this._blockBuilder
                .WithBlockIndex(this._blockchainState.LastBlockIndex)
                .WithBlockId(this._blockchainState.CurrentBlockId)
                .WithPreviousBlockId(this._blockchainState.CurrentPreviousBlockId)
                .WithNextBlockId(this._blockchainState.CurrentNextBlockId)
                .WithRewardBeneficiary(
                    this._configuration["StackerInfo:PublicSigningAddress"], 
                    this._configuration["StackerInfo:PrivateSigningKey"], 
                    this._blockchainState.LastBlockIndex)
                .WithTransactions(transactions)
                .Build();

            await this._eventAggregator.PublishAsync(this._blockCreatedEventFactory.GetInstance(block, this._blockchainState));
        });
    }
}
