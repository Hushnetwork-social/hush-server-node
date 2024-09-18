using Olimpo;
using Microsoft.Extensions.Logging;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Blockchain.Builders;
using HushServerNode.InternalModule.Blockchain.Cache;
using HushServerNode.InternalModule.Blockchain.Events;

namespace HushServerNode.InternalModule.Blockchain;

public class BlockchainService : 
    IBlockchainService,
    IHandleAsync<BlockCreatedEvent>
{
    private readonly IBlockchainDbAccess _blockchainDbAccess;
    private readonly IBlockBuilder _blockBuilder;
    private readonly IBlockVerifier _blockVerifier;
    private readonly IBlockchainStatus _blockchainStatus;
    private readonly TransactionBaseConverter _transactionBaseConverter;
    private readonly IEnumerable<IIndexStrategy> _indexStrategies;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<BlockchainService> _logger;

    public BlockchainService(
        IBlockchainDbAccess blockchainDbAccess,
        IBlockBuilder blockBuilder,
        IBlockVerifier blockVerifier,
        IBlockchainStatus blockchainStatus,
        TransactionBaseConverter transactionBaseConverter,
        IEnumerable<IIndexStrategy> indexStrategies, 
        IEventAggregator eventAggregator,
        ILogger<BlockchainService> logger)
    {
        this._blockchainDbAccess = blockchainDbAccess;
        this._blockBuilder = blockBuilder;
        this._blockVerifier = blockVerifier;
        this._blockchainStatus = blockchainStatus;
        this._transactionBaseConverter = transactionBaseConverter;
        this._indexStrategies = indexStrategies;
        this._eventAggregator = eventAggregator;
        this._logger = logger;

        this._eventAggregator.Subscribe(this);
    }

    public async Task InitializeBlockchainAsync()
    {
        this._logger.LogInformation("Initializing Blockchain...");

        var hasBlockchainState = await this._blockchainDbAccess.HasBlockchainStateAsync();
        if (!hasBlockchainState)
        {
            // initialize the blockchain from genesis block.

            var blockchainState = new BlockchainState
            {
                BlockchainStateId = Guid.NewGuid(),
                LastBlockIndex = 1,
                CurrentPreviousBlockId = string.Empty,
                CurrentBlockId = Guid.NewGuid().ToString(),
                CurrentNextBlockId = Guid.NewGuid().ToString(),
            };

            var genesisBlock = this._blockBuilder
                .WithBlockIndex(blockchainState.LastBlockIndex)
                .WithBlockId(blockchainState.CurrentBlockId)
                .WithNextBlockId(blockchainState.CurrentNextBlockId)
                .WithRewardBeneficiary(
                    this._blockchainStatus.PublicSigningAddress,
                    this._blockchainStatus.PrivateSigningKey,
                    this._blockchainStatus.BlockIndex)
                .Build();

            this._logger.LogInformation("Creating Genesis Block - {0} | Next Block - {1}", this._blockchainStatus.BlockId, this._blockchainStatus.NextBlockId);

            if (this._blockVerifier.IsBlockValid(genesisBlock))
            {
                await this._blockchainDbAccess.SaveBlockAndBlockchainStateAsync(
                    genesisBlock.ToBlockEntity(this._transactionBaseConverter), 
                    blockchainState);
                
                this.IndexBlock(genesisBlock);
            }
            else
            {
                throw new InvalidOperationException("This exception should never happen. The genesis block is invalid.");
            }
        }

        await this._blockchainStatus.LoadBlockchainStatus();
        await this._eventAggregator.PublishAsync(new BlockchainInitializedEvent());
    }

    public async Task HandleAsync(BlockCreatedEvent message)
    {
        if (this._blockVerifier.IsBlockValid(message.Block))
        {
            // await this.SaveBlock(message.Block);
            await this._blockchainDbAccess.SaveBlockAndBlockchainStateAsync(
                    message.Block.ToBlockEntity(this._transactionBaseConverter), 
                    message.BlockchainState);

            this.IndexBlock(message.Block);

            this._logger.LogInformation("Creating Block: {0} | Previous Block: {1} | Next Block: {2}", 
                this._blockchainStatus.BlockId, 
                this._blockchainStatus.PreviousBlockId,  
                this._blockchainStatus.NextBlockId);

            // TODO [AboimPinto]: Signal the MemPool the created event to remove the transactions from the MemPool.
        }
        else
        {
            // TODO [AboimPinto]: what we should do when the block is not valid?
            this._logger.LogError("Block is not valid.");
        }
    }

    private void IndexBlock(Block block)
    {
        foreach(var transaction in block.Transactions)
        {
            var indexStrategiesThatCanHandle = this._indexStrategies
                .Where(x => x.CanHandle(transaction));
                
            foreach (var item in indexStrategiesThatCanHandle)
            {
                item.Handle(transaction);
            }
        }
    }
}
