using HushNode.Blockchain.Events;
using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Persistency.Abstractions.Models.Block;
using HushNode.Blockchain.Persistency.Abstractions.Models.Block.States;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction.States;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;
using HushNode.Blockchain.Persistency.EntityFramework;
using HushNode.Credentials;
using HushNode.Interfaces;
using HushNode.InternalPayloads;
using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Blockchain.Workflows;

public class BlockAssemblerWorkflow(
    ICredentialsProvider credentialsProvider,
    IUnitOfWorkProvider<BlockchainDbContext> unitOfWorkProvider,
    IEventAggregator eventAggregator,
    ILogger<BlockAssemblerWorkflow> logger) : IBlockAssemblerWorkflow
{
    private readonly ICredentialsProvider _credentialsProvider = credentialsProvider;
    private readonly IUnitOfWorkProvider<BlockchainDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly ILogger<BlockAssemblerWorkflow> _logger = logger;

    public async Task AssembleGenesisBlockAsync(BlockchainState genesisBlockchainState)
    {
        var genesisUnsignedBlock = UnsignedBlockHandler.CreateGenesis(
            Timestamp.Current, 
            genesisBlockchainState.NextBlockId);

        var blockProducerCredentials = this._credentialsProvider.GetCredentials();

        var validatedRewardTransaction = RewardPayloadHandler
            .CreateRewardTransaction("HUSH", DecimalStringConverter.DecimalToString(5m))
            .SignByUser(blockProducerCredentials.PublicSigningAddress, blockProducerCredentials.PrivateSigningKey)
            .SignByValidator(blockProducerCredentials.PublicSigningAddress, blockProducerCredentials.PrivateSigningKey);

        var genesisBlockWithRewardTransactions = genesisUnsignedBlock with
        {
            Transactions = [..genesisUnsignedBlock.Transactions, validatedRewardTransaction]
        };

        var finalizedGenegisBlock =genesisBlockWithRewardTransactions
            .SignIt(
                blockProducerCredentials.PublicSigningAddress, 
                blockProducerCredentials.PrivateSigningKey)
            .FinalizeIt();

        var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();
        await writableUnitOfWork
            .GetRepository<IBlockRepository>()
            .AddBlockchainBlockAsync(finalizedGenegisBlock.ToBlockchainBlock());

        await writableUnitOfWork
            .GetRepository<IBlockchainStateRepository>()
            .SetBlockchainStateAsync(genesisBlockchainState);
        await writableUnitOfWork.CommitAsync();

        this._logger.LogInformation("Genesis block {0} generated...", finalizedGenegisBlock.BlockId);

        await this._eventAggregator.PublishAsync(new BlockCreatedEvent(finalizedGenegisBlock.BlockId));
    }

    public async Task AsembleBlockAsync(
        BlockchainState blockchainState,
        IReadOnlyList<AbstractTransaction> transactions)
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

        var validatedRewardTransaction = RewardPayloadHandler
            .CreateRewardTransaction("HUSH", DecimalStringConverter.DecimalToString(5m))
            .SignByUser(blockProducerCredentials.PublicSigningAddress, blockProducerCredentials.PrivateSigningKey)
            .SignByValidator(blockProducerCredentials.PublicSigningAddress, blockProducerCredentials.PrivateSigningKey);

        var unsignedBlockWithTransactions = unsignedBlock with
        {
            Transactions = [..unsignedBlock.Transactions, validatedRewardTransaction, ..transactions]
        };

        var finalizedBlock = unsignedBlockWithTransactions
            .SignIt(
                blockProducerCredentials.PublicSigningAddress, 
                blockProducerCredentials.PrivateSigningKey)
            .FinalizeIt();

        var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();
        await writableUnitOfWork
            .GetRepository<IBlockRepository>()
            .AddBlockchainBlockAsync(finalizedBlock.ToBlockchainBlock());

        await writableUnitOfWork
            .GetRepository<IBlockchainStateRepository>()
            .SetBlockchainStateAsync(newBlockchainState);
        await writableUnitOfWork.CommitAsync();
        
        this._logger.LogInformation($"Block {0} generated...", finalizedBlock.BlockId);

        await this._eventAggregator.PublishAsync(new BlockCreatedEvent(finalizedBlock.BlockId));
    }
}
