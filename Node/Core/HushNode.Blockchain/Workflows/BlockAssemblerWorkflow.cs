using HushNode.Blockchain.Events;
using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Persistency.Abstractions.Models.Block;
using HushNode.Blockchain.Persistency.Abstractions.Models.Block.States;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction.States;
using HushNode.Credentials;
using HushNode.Interfaces;
using HushNode.InternalPayloads;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Blockchain.Workflows;

public class BlockAssemblerWorkflow(
    ICredentialsProvider credentialsProvider,
    IUnitOfWorkFactory unitOfWorkFactory,
    IEventAggregator eventAggregator,
    ILogger<BlockAssemblerWorkflow> logger) : IBlockAssemblerWorkflow
{
    private readonly ICredentialsProvider _credentialsProvider = credentialsProvider;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory = unitOfWorkFactory;
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

        using (var unitOfWork = this._unitOfWorkFactory.Create())
        {
            await unitOfWork.BlockRepository.AddBlockchainBlockAsync(finalizedGenegisBlock.ToBlockchainBlock());
            await unitOfWork.BlockStateRepository.SetBlockchainStateAsync(genesisBlockchainState);
            await unitOfWork.CommitAsync();
        }

        this._logger.LogInformation($"Genesis block {finalizedGenegisBlock.BlockId} generated...");

        await this._eventAggregator.PublishAsync(new BlockCreatedEvent());
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

        using (var unitOfWork = this._unitOfWorkFactory.Create())
        {
            await unitOfWork.BlockRepository.AddBlockchainBlockAsync(finalizedBlock.ToBlockchainBlock());
            await unitOfWork.BlockStateRepository.SetBlockchainStateAsync(newBlockchainState);
            await unitOfWork.CommitAsync();
        }
        
        this._logger.LogInformation(
            $"Block {finalizedBlock.BlockId} generated...");

        await this._eventAggregator.PublishAsync(new BlockCreatedEvent());
    }
}
