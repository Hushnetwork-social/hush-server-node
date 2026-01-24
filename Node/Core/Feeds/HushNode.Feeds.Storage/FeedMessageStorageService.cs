using Olimpo.EntityFramework.Persistency;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

public class FeedMessageStorageService(
    IUnitOfWorkProvider<FeedsDbContext> unitOfWorkProvider)
    : IFeedMessageStorageService
{
    private readonly IUnitOfWorkProvider<FeedsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public async Task CreateFeedMessageAsync(FeedMessage feedMessage)
    {
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        await writableUnitOfWork
            .GetRepository<IFeedMessageRepository>()
            .CreateFeedMessageAsync(feedMessage);

        await writableUnitOfWork
            .CommitAsync();
    }

    public async Task<IEnumerable<FeedMessage>> RetrieveLastFeedMessagesForAddressAsync(string publicSigningAddress, BlockIndex blockIndex)
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();

        return await readOnlyUnitOfWork
            .GetRepository<IFeedMessageRepository>()
            .RetrieveLastFeedMessagesForAddressAsync(publicSigningAddress, blockIndex); 
    }

    public async Task<IEnumerable<FeedMessage>> RetrieveLastFeedMessagesForFeedAsync(FeedId feedId, BlockIndex blockIndex)
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();

        return await readOnlyUnitOfWork
            .GetRepository<IFeedMessageRepository>()
            .RetrieveMessagesForFeedAsync(feedId, blockIndex);
    }

    public async Task<FeedMessage?> GetFeedMessageByIdAsync(FeedMessageId messageId)
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();

        return await readOnlyUnitOfWork
            .GetRepository<IFeedMessageRepository>()
            .GetFeedMessageByIdAsync(messageId);
    }

    public async Task<PaginatedMessagesResult> GetPaginatedMessagesAsync(
        FeedId feedId,
        BlockIndex sinceBlockIndex,
        int limit,
        bool fetchLatest)
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();

        return await readOnlyUnitOfWork
            .GetRepository<IFeedMessageRepository>()
            .GetPaginatedMessagesAsync(feedId, sinceBlockIndex, limit, fetchLatest);
    }
}
