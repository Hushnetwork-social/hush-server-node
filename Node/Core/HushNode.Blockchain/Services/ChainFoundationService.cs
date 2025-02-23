using Microsoft.Extensions.Logging;
using Olimpo;
using HushNode.Blockchain.Events;
using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Workflows;

namespace HushNode.Blockchain.Services;

public class ChainFoundationService(
    IBlockAssemblerWorkflow blockAssemblerWorkflow,
    IUnitOfWorkFactory unitOfWorkFactory,
    IEventAggregator eventAggregator,
    ILogger<ChainFoundationService> logger) : IChainFoundationService
{
    private readonly ILogger<ChainFoundationService> _logger = logger;
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow = blockAssemblerWorkflow;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory = unitOfWorkFactory;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task InitializeChain()
    {
        this._logger.LogInformation("Initializion Blockchain...");

        var readOnlyUnitOfWork = this._unitOfWorkFactory.CreateReadOnly();
        var blockchainState = await readOnlyUnitOfWork.BlockStateRepository.GetCurrentStateAsync();

        Func<BlockchainState, Task> handler = blockchainState switch
        {
            GenesisBlockchainState => GenerateGenesisBlock,
            _ => BlockchainInitializationFinished
        };
        
        await handler.Invoke(blockchainState);
    }

    private async Task BlockchainInitializationFinished(BlockchainState blockchainState)
    {
        await this._eventAggregator.PublishAsync(new BlockchainInitializedEvent());
    }

    private async Task GenerateGenesisBlock(BlockchainState genesisBlockchainState)
    {
        await this._blockAssemblerWorkflow.AssembleGenesisBlockAsync(genesisBlockchainState);
        await this._eventAggregator.PublishAsync(new BlockchainInitializedEvent());
    }
}
