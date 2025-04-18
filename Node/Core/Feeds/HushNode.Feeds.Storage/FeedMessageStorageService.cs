using HushShared.Feeds.Model;
using Olimpo.EntityFramework.Persistency;

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
}
