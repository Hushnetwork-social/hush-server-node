using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Persistency.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace HushNode.Blockchain.Services;

public class ChainFoundationService : IChainFoundationService
{
    private readonly ILogger<ChainFoundationService> _logger;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IUnitOfWork _unitOfWork;

    public ChainFoundationService(
        IUnitOfWorkFactory unitOfWorkFactory,
        ILogger<ChainFoundationService> logger)
    {
        this._unitOfWorkFactory = unitOfWorkFactory;
        this._logger = logger;

        this._unitOfWork = this._unitOfWorkFactory.Create();
    }

    public async Task InitializeChain()
    {
        this._logger.LogInformation("Initializion Blockchain...");

        var blockchainState = await this._unitOfWork.BlockStateRepository.GetCurrentStateAsync();

        var handler = blockchainState switch
        {
            GenesisBlockchainState => (Action)GenerateGenesisBlock,
            _ => (Action)BlockchainInitializationFinished
        };
        
        handler.Invoke();
    }

    private void BlockchainInitializationFinished()
    {
    }

    private void GenerateGenesisBlock()
    {
    }
}
