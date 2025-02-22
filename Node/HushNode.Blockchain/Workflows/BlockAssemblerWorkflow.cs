using HushNode.Blockchain.Events;
using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Persistency.Abstractions.Models.Block.States;
using HushNode.Blockchain.Persistency.Abstractions.Models.Transaction.States;
using HushNode.Credentials;
using HushNode.Intefaces;
using HushNode.InternalPayloads;
using Olimpo;

namespace HushNode.Blockchain.Workflows;

public class BlockAssemblerWorkflow(
    ICredentialsProvider credentialsProvider,
    IUnitOfWorkFactory unitOfWorkFactory,
    IEventAggregator eventAggregator) : IBlockAssemblerWorkflow
{
    private readonly ICredentialsProvider _credentialsProvider = credentialsProvider;
    private readonly IUnitOfWork _unitOfWork = unitOfWorkFactory.Create();
    private readonly IEventAggregator _eventAggregator = eventAggregator;

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

        await this._unitOfWork.BlockRepository.AddBlockchainBlockAsync(finalizedGenegisBlock.ToBlockchainBlock());
        await this._unitOfWork.BlockStateRepository.SetBlockchainStateAsync(genesisBlockchainState);

        await this._unitOfWork.CommitAsync();

        await this._eventAggregator.PublishAsync(new BlockCreatedEvent());
    }
}
