using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading.Tasks;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushServerNode.ApplicationSettings;
using HushServerNode.Blockchain.Builders;
using HushServerNode.Blockchain.Events;
using HushServerNode.Blockchain.IndexStrategies;
using HushServerNode.CacheService;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushServerNode.Blockchain;

public class BlockchainService : 
    IBootstrapper, 
    IBlockchainService,
    IHandleAsync<BlockCreatedEvent>
{
    private readonly ILogger<BlockchainService> _logger;

    private readonly IEventAggregator _eventAggregator;
    private readonly IBlockBuilder _blockBuilder;
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly IBlockVerifier _blockVerifier;
    // private readonly IBlockchainDb _blockchainDb;
    private readonly IBlockchainCache _blockchainCache;
    // private readonly IBlockchainIndexDb _blockchainIndexDb;
    private readonly TransactionBaseConverter _transactionBaseConverter;
    private readonly IEnumerable<IIndexStrategy> _indexStrategies;
    private Block _currentBlock;

    public BlockchainState BlockchainState { get; set; }

    public BlockchainService(
        IEventAggregator eventAggregator,
        IBlockBuilder blockBuilder,
        IApplicationSettingsService applicationSettingsService,
        IBlockVerifier blockVerifier,
        IBlockchainCache blockchainCache,
        TransactionBaseConverter transactionBaseConverter,
        IEnumerable<IIndexStrategy> indexStrategies, 
        ILogger<BlockchainService> logger)
    {
        this._eventAggregator = eventAggregator;
        this._blockBuilder = blockBuilder;
        this._applicationSettingsService = applicationSettingsService;
        this._blockVerifier = blockVerifier;
        this._blockchainCache = blockchainCache;
        this._transactionBaseConverter = transactionBaseConverter;
        this._indexStrategies = indexStrategies;
        this._logger = logger;

        this._eventAggregator.Subscribe(this);
    }

    public int Priority { get; set; } = 10;

    public Subject<bool> BootstrapFinished { get; }

    public async Task InitializeBlockchainAsync()
    {
        this._logger.LogInformation("Initializing Blockchain...");

        this.BlockchainState = await this._blockchainCache.GetBlockchainStateAsync();
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
                .WithRewardBeneficiary(this._applicationSettingsService.StackerInfo, this.BlockchainState.LastBlockIndex)
                .Build();


            // this._blockchainDb.AddBlock(genesisBlock);
            this._currentBlock = genesisBlock;
            this._logger.LogInformation("Creating Genesis Block - {0} | Next Block - {1}", this.BlockchainState.CurrentBlockId, this.BlockchainState.CurrentNextBlockId);


            await this.UpdateBlockchainState();
            await this.SaveBlock(genesisBlock);
            this.IndexBlock(genesisBlock);
        }

        await this._eventAggregator.PublishAsync(new BlockchainInitializedEvent(this.BlockchainState.CurrentBlockId, this.BlockchainState.CurrentNextBlockId, this.BlockchainState.LastBlockIndex));
    }

    public IEnumerable<VerifiedTransaction> ListTransactionsForAddress(string address, double lastHeightSynched)
    {
        // if (this._blockchainIndexDb.GroupedTransactions.ContainsKey(address))
        // {
        //     return this._blockchainIndexDb.GroupedTransactions[address]
        //         .Where(x => 
        //             x.SpecificTransaction.Issuer == address && 
        //             x.BlockIndex > lastHeightSynched)
        //         .OrderBy(x => x.BlockIndex);
        // }

        return new List<VerifiedTransaction>();
    }

    public double GetBalanceForAddress(string address)
    {
        return this._blockchainCache.GetBalance(address);
    }

    public HushUserProfile GetUserProfile(string publicAddress)
    {
        var profileEntity = this._blockchainCache.GetProfile(publicAddress);

        return new HushUserProfile
        {
            UserName = profileEntity.UserName,
            UserPublicSigningAddress = profileEntity.PublicSigningAddress,
            UserPublicEncryptAddress = profileEntity.PublicEncryptAddress,
            IsPublic = profileEntity.IsPublic
        };
    }

    public void Shutdown()
    {
    }

    public async Task Startup()
    {
        await this.InitializeBlockchainAsync();
    }

    public async Task HandleAsync(BlockCreatedEvent message)
    {
        if (this._blockVerifier.IsBlockValid(message.Block))
        {
            await this.UpdateBlockchainState();
            await this.SaveBlock(message.Block);

            // this._blockchainDb.AddBlock(message.Block);
            this._currentBlock = message.Block;

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
        }
    }
    
    public async Task UpdateBlockchainState()
    {
        await this._blockchainCache.UpdateBlockchainState(this.BlockchainState);
    }

    public async Task SaveBlock(Block block)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            Converters = { this._transactionBaseConverter }
        };

        await this._blockchainCache.SaveBlockAsync(
            block.BlockId, 
            block.Index, 
            block.PreviousBlockId, 
            block.NextBlockId, 
            block.Hash, 
            block.ToJson(jsonOptions));
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
