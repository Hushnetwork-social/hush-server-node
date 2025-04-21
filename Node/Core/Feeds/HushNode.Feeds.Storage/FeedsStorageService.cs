using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.Storage;

public class FeedsStorageService(
    IUnitOfWorkProvider<FeedsDbContext> unitOfWorkProvider) 
    : IFeedsStorageService
{
    private readonly IUnitOfWorkProvider<FeedsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public async Task<bool> HasPersonalFeed(string publicSigningAddress) =>
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .HasPersonalFeed(publicSigningAddress);

    public async Task CreateFeed(Feed feed)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        try
        {
            await writableUnitOfWork
                .GetRepository<IFeedsRepository>()
                .CreateFeed(feed);

            await writableUnitOfWork.CommitAsync();
        }
        catch (Exception ex)
        {
            
        }
    }

    public async Task<IEnumerable<Feed>> RetrieveFeedsForAddress(string publicSigningAddress, BlockIndex blockIndex) => 
        await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedsRepository>()
            .RetrieveFeedsForAddress(publicSigningAddress, blockIndex);
}
