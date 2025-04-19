using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public class FeedMessageStorageService(
    IUnitOfWorkProvider<FeedsDbContext> unitOfWorkProvider)
    : IFeedMessageStorageService
{
    private readonly IUnitOfWorkProvider<FeedsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public async Task CreateFeedMessage(FeedMessage feedMessage)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedMessageRepository>()
            .CreateFeedMessage(feedMessage);

        await writableUnitOfWork
            .CommitAsync();
    }

    public async Task<IEnumerable<FeedMessage>> RetrieveLastFeedMessagesForAddress(string publicSigningAddress, BlockIndex blockIndex)
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();

        return await readOnlyUnitOfWork
            .GetRepository<IFeedMessageRepository>()
            .RetrieveLastFeedMessagesForAddress(publicSigningAddress, blockIndex); 
    }
}
