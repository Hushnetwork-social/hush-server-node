using System.Text.Json;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushServerNode.Interfaces;
using HushServerNode.InternalModule.Blockchain.Builders;
using HushServerNode.InternalModule.Blockchain.Cache;
using HushServerNode.InternalModule.Blockchain.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushServerNode.InternalModule.Blockchain;

public class BlockchainService : 
    IBlockchainService,
    IHandleAsync<BlockCreatedEvent>
{
    private readonly IBlockchainDbAccess _blockchainDbAccess;
    private readonly IBlockBuilder _blockBuilder;
    private readonly IBlockVerifier _blockVerifier;
    private readonly TransactionBaseConverter _transactionBaseConverter;
    private readonly IEnumerable<IIndexStrategy> _indexStrategies;
    private readonly IConfiguration _configuration;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<BlockchainService> _logger;

    public BlockchainState BlockchainState { get; private set; }

    public BlockchainService(
        IBlockchainDbAccess blockchainDbAccess,
        IBlockBuilder blockBuilder,
        IBlockVerifier blockVerifier,
        TransactionBaseConverter transactionBaseConverter,
        IEnumerable<IIndexStrategy> indexStrategies, 
        IConfiguration configuration,
        IEventAggregator eventAggregator,
        ILogger<BlockchainService> logger)
    {
        this._blockchainDbAccess = blockchainDbAccess;
        this._blockBuilder = blockBuilder;
        this._blockVerifier = blockVerifier;
        this._transactionBaseConverter = transactionBaseConverter;
        this._indexStrategies = indexStrategies;
        this._configuration = configuration;
        this._eventAggregator = eventAggregator;
        this._logger = logger;

        this._eventAggregator.Subscribe(this);
    }

    public async Task InitializeBlockchainAsync()
    {
        this._logger.LogInformation("Initializing Blockchain...");

        this.BlockchainState = await this._blockchainDbAccess.GetBlockchainStateAsync();
        if (this.BlockchainState == null)
        {
            // initialize the blockchain from genesis block.
            this.BlockchainState = new BlockchainState()
            {
                BlockchainStateId = Guid.NewGuid(),
                LastBlockIndex = 1,
                CurrentBlockId = Guid.NewGuid().ToString(),
                CurrentPreviousBlockId = string.Empty,
                CurrentNextBlockId = Guid.NewGuid().ToString(),
            };

            var genesisBlock = this._blockBuilder
                .WithBlockIndex(this.BlockchainState.LastBlockIndex)
                .WithBlockId(this.BlockchainState.CurrentBlockId)
                .WithNextBlockId(this.BlockchainState.CurrentNextBlockId)
                .WithRewardBeneficiary(
                    this._configuration["StackerInfo:PublicSigningAddress"], 
                    this._configuration["StackerInfo:PrivateSigningKey"], 
                    this.BlockchainState.LastBlockIndex)
                .Build();

            this._logger.LogInformation("Creating Genesis Block - {0} | Next Block - {1}", this.BlockchainState.CurrentBlockId, this.BlockchainState.CurrentNextBlockId);

            if (this._blockVerifier.IsBlockValid(genesisBlock))
            {
                await this._blockchainDbAccess.UpdateBlockchainState(this.BlockchainState);
                await this.SaveBlock(genesisBlock);
                this.IndexBlock(genesisBlock);
            }
            else
            {
                throw new InvalidOperationException("This exception should never happen. The genesis block is invalid.");
            }
        }

        await this._eventAggregator.PublishAsync(new BlockchainInitializedEvent(this.BlockchainState));
    }

    public async Task SaveBlock(Block block)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            Converters = { this._transactionBaseConverter }
        };

        await this._blockchainDbAccess.SaveBlockAsync(
            block.BlockId, 
            block.Index, 
            block.PreviousBlockId, 
            block.NextBlockId, 
            block.Hash, 
            block.ToJson(jsonOptions));
    }

    public async Task HandleAsync(BlockCreatedEvent message)
    {
        if (this._blockVerifier.IsBlockValid(message.Block))
        {
            this.BlockchainState = message.BlockchainState;
            await this._blockchainDbAccess.UpdateBlockchainState(this.BlockchainState);
            await this.SaveBlock(message.Block);

            this.IndexBlock(message.Block);

            this._logger.LogInformation("Creating Block: {0} | Previous Block: {1} | Next Block: {2}", 
                this.BlockchainState.CurrentBlockId, 
                this.BlockchainState.CurrentPreviousBlockId,  
                this.BlockchainState.CurrentNextBlockId);

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
