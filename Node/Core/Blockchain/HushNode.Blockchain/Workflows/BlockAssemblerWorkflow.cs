using Microsoft.Extensions.Logging;
using Olimpo;
using HushNode.Bank;
using HushNode.Blockchain.BlockModel.States;
using HushNode.Blockchain.Storage;
using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Events;
using HushShared.Blockchain;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.Model;
using HushShared.Converters;
using HushShared.Blockchain.BlockModel;

namespace HushNode.Blockchain.Workflows;

public class BlockAssemblerWorkflow(
    ICredentialsProvider credentialsProvider,
    IBlockchainStorageService blockchainStorageService,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator,
    ILogger<BlockAssemblerWorkflow> logger) : IBlockAssemblerWorkflow
{
    private readonly ICredentialsProvider _credentialsProvider = credentialsProvider;
    private readonly IBlockchainStorageService _blockchainStorageService = blockchainStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly ILogger<BlockAssemblerWorkflow> _logger = logger;

    public async Task AssembleGenesisBlockAsync()
    {
        var genesisUnsignedBlock = UnsignedBlockHandler.CreateGenesis(
            Timestamp.Current, 
            this._blockchainCache.NextBlockId);

        var blockProducerCredentials = this._credentialsProvider.GetCredentials();
        var validatedRewardTransaction = CreateAndSignRewardTransaction(blockProducerCredentials);

        var genesisBlockWithRewardTransactions = genesisUnsignedBlock with
        {
            Transactions = [..genesisUnsignedBlock.Transactions, validatedRewardTransaction]
        };

        var finalizedGenegisBlock = genesisBlockWithRewardTransactions
            .SignAndFinalizeBlock(blockProducerCredentials);

        await this._blockchainStorageService
            .PersisteBlockAndBlockState(finalizedGenegisBlock.ToBlockchainBlock());

        this._logger.LogInformation("Genesis block {0} generated...", finalizedGenegisBlock.BlockId);

        await this._eventAggregator.PublishAsync(new BlockCreatedEvent(finalizedGenegisBlock));
    }

    public async Task AssembleBlockAsync(
        IEnumerable<AbstractTransaction> transactions)
    {
        this._blockchainCache
            .SetBlockIndex(new BlockIndex(this._blockchainCache.LastBlockIndex.Value + 1))
            .SetPreviousBlockId(this._blockchainCache.CurrentBlockId)
            .SetCurrentBlockId(this._blockchainCache.NextBlockId)
            .SetNextBlockId(new BlockId(Guid.NewGuid()));

        var unsignedBlock = UnsignedBlockHandler.CreateNew(
            this._blockchainCache.CurrentBlockId,
            this._blockchainCache.LastBlockIndex,
            Timestamp.Current, 
            this._blockchainCache.PreviousBlockId,
            this._blockchainCache.NextBlockId);

        var blockProducerCredentials = this._credentialsProvider.GetCredentials();
        var validatedRewardTransaction = CreateAndSignRewardTransaction(blockProducerCredentials);

        var unsignedBlockWithTransactions = unsignedBlock with
        {
            Transactions = [..unsignedBlock.Transactions, validatedRewardTransaction, ..transactions]
        };
        var finalizedBlock = unsignedBlockWithTransactions
            .SignAndFinalizeBlock(blockProducerCredentials);

        await this._blockchainStorageService
            .PersisteBlockAndBlockState(finalizedBlock.ToBlockchainBlock());

        this._logger.LogInformation($"Block {0} generated...", finalizedBlock.BlockId);

        await this._eventAggregator.PublishAsync(new BlockCreatedEvent(finalizedBlock));
    }

    private static AbstractTransaction CreateAndSignRewardTransaction(CredentialsProfile blockProducerCredentials) => 
        RewardPayloadHandler
            .CreateRewardTransaction("HUSH", DecimalStringConverter.DecimalToString(5m))
            .SignByUser(
                blockProducerCredentials.PublicSigningAddress, 
                blockProducerCredentials.PrivateSigningKey)
            .SignByValidator(
                blockProducerCredentials.PublicSigningAddress, 
                blockProducerCredentials.PrivateSigningKey);
}
