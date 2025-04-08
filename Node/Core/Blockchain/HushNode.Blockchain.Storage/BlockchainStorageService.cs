using Olimpo.EntityFramework.Persistency;
using HushNode.Blockchain.Storage.Model;
using HushShared.Caching;

namespace HushNode.Blockchain.Storage;

public class BlockchainStorageService(
    IUnitOfWorkProvider<BlockchainDbContext> unitOfWorkProvider,
    IBlockchainCache blockchainCache) 
    : IBlockchainStorageService
{
    private readonly IUnitOfWorkProvider<BlockchainDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;

    public async Task<BlockchainState> RetrieveCurrentBlockchainStateAsync()
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        return await readOnlyUnitOfWork
            .GetRepository<IBlockchainStateRepository>()
            .GetCurrentStateAsync();
    }

    public async Task PersisteBlockAndBlockState(BlockchainBlock blockchainBlock)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();
        await writableUnitOfWork
            .GetRepository<IBlockRepository>()
            .AddBlockchainBlockAsync(blockchainBlock);

        if (this._blockchainCache.BlockchainStateInDatabase)
        {
            await writableUnitOfWork
                .GetRepository<IBlockchainStateRepository>()
                .UpdateBlockchainStateAsync(this._blockchainCache);
        }
        else
        {
            await writableUnitOfWork
                .GetRepository<IBlockchainStateRepository>()
                .InsertBlockchainStateAsync(this._blockchainCache);
        }

        await writableUnitOfWork.CommitAsync();
    }
}
