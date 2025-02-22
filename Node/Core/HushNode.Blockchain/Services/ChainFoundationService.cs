using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Workflows;
using Microsoft.Extensions.Logging;

namespace HushNode.Blockchain.Services;

public class ChainFoundationService : IChainFoundationService
{
    private readonly ILogger<ChainFoundationService> _logger;
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IUnitOfWork _unitOfWork;

    public ChainFoundationService(
        IBlockAssemblerWorkflow blockAssemblerWorkflow,
        IUnitOfWorkFactory unitOfWorkFactory,
        ILogger<ChainFoundationService> logger)
    {
        this._blockAssemblerWorkflow = blockAssemblerWorkflow;
        this._unitOfWorkFactory = unitOfWorkFactory;
        this._logger = logger;

        this._unitOfWork = this._unitOfWorkFactory.Create();
    }

    public async Task InitializeChain()
    {
        this._logger.LogInformation("Initializion Blockchain...");

        var blockchainState = await this._unitOfWork.BlockStateRepository.GetCurrentStateAsync();

        Func<BlockchainState, Task> handler = blockchainState switch
        {
            GenesisBlockchainState => GenerateGenesisBlock,
            _ => BlockchainInitializationFinished
        };
        
        await handler.Invoke(blockchainState);
    }

    private Task BlockchainInitializationFinished(BlockchainState blockchainState)
    {
        return Task.CompletedTask;
    }

    private async Task GenerateGenesisBlock(BlockchainState genesisBlockchainState)
    {
        await this._blockAssemblerWorkflow.AssembleGenesisBlockAsync(genesisBlockchainState);
    }
}
