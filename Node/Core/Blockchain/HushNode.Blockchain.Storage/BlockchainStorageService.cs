using HushNode.Blockchain.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Blockchain.Storage;

public class BlockchainStorageService(
    IUnitOfWorkProvider<BlockchainDbContext> unitOfWorkProvider) 
    : IBlockchainStorageService
{
    private readonly IUnitOfWorkProvider<BlockchainDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public async Task<BlockchainState> RetrieveCurrentBlockchainStateAsync()
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        return await readOnlyUnitOfWork
            .GetRepository<IBlockchainStateRepository>()
            .GetCurrentStateAsync();
    }

    public async Task PersisteBlockAndBlockState(BlockchainBlock blockchainBlock, BlockchainState blockchainState)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();
        await writableUnitOfWork
            .GetRepository<IBlockRepository>()
            .AddBlockchainBlockAsync(blockchainBlock);

        await writableUnitOfWork
            .GetRepository<IBlockchainStateRepository>()
            .SetBlockchainStateAsync(blockchainState);
        await writableUnitOfWork.CommitAsync();
    }
}
