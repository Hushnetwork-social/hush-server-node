using Microsoft.Extensions.Logging;
using Olimpo;
using HushNode.Bank;
using HushNode.Blockchain.BlockModel;
using HushNode.Blockchain.BlockModel.States;
using HushNode.Blockchain.Storage;
using HushNode.Blockchain.Storage.Model;
using HushNode.Credentials;
using HushNode.Events;
using HushShared.Blockchain;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.Model;
using HushShared.Converters;

namespace HushNode.Blockchain.Workflows;

public class BlockAssemblerWorkflow(
    ICredentialsProvider credentialsProvider,
    IBlockchainStorageService blockchainStorageService,
    IEventAggregator eventAggregator,
    ILogger<BlockAssemblerWorkflow> logger) : IBlockAssemblerWorkflow
{
    private readonly ICredentialsProvider _credentialsProvider = credentialsProvider;
    private readonly IBlockchainStorageService _blockchainStorageService = blockchainStorageService;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly ILogger<BlockAssemblerWorkflow> _logger = logger;

    public async Task AssembleGenesisBlockAsync(BlockchainState genesisBlockchainState)
    {
        var genesisUnsignedBlock = UnsignedBlockHandler.CreateGenesis(
            Timestamp.Current, 
            genesisBlockchainState.NextBlockId);

        var blockProducerCredentials = this._credentialsProvider.GetCredentials();
        var validatedRewardTransaction = CreateAndSignRewardTransaction(blockProducerCredentials);

        var genesisBlockWithRewardTransactions = genesisUnsignedBlock with
        {
            Transactions = [..genesisUnsignedBlock.Transactions, validatedRewardTransaction]
        };

        var finalizedGenegisBlock = genesisBlockWithRewardTransactions
            .SignAndFinalizeBlock(blockProducerCredentials);

        await this._blockchainStorageService.PersisteBlockAndBlockState(
            finalizedGenegisBlock.ToBlockchainBlock(), 
            genesisBlockchainState);

        this._logger.LogInformation("Genesis block {0} generated...", finalizedGenegisBlock.BlockId);

        await this._eventAggregator.PublishAsync(new BlockCreatedEvent(finalizedGenegisBlock));
    }

    public async Task AssembleBlockAsync(
        BlockchainState blockchainState,
        IEnumerable<AbstractTransaction> transactions)
    {
        var newBlockchainState = new BlockchainState(
            blockchainState.BlockchainStateId,
            new BlockIndex(blockchainState.BlockIndex.Value + 1),
            blockchainState.NextBlockId,
            blockchainState.CurrentBlockId,
            new BlockId(Guid.NewGuid()));

        var unsignedBlock = UnsignedBlockHandler.CreateNew(
            newBlockchainState.CurrentBlockId,
            newBlockchainState.BlockIndex,
            Timestamp.Current, 
            newBlockchainState.PreviousBlockId,
            newBlockchainState.NextBlockId);

        var blockProducerCredentials = this._credentialsProvider.GetCredentials();
        var validatedRewardTransaction = CreateAndSignRewardTransaction(blockProducerCredentials);

        var unsignedBlockWithTransactions = unsignedBlock with
        {
            Transactions = [..unsignedBlock.Transactions, validatedRewardTransaction, ..transactions]
        };
        var finalizedBlock = unsignedBlockWithTransactions
            .SignAndFinalizeBlock(blockProducerCredentials);

        await this._blockchainStorageService.PersisteBlockAndBlockState(
            finalizedBlock.ToBlockchainBlock(), 
            newBlockchainState
        );

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
