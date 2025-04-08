using Olimpo.EntityFramework.Persistency;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;


namespace HushNode.Feeds;

public class NewFeedTransactionHandler(
    IUnitOfWorkProvider<FeedsDbContext> unitOfWorkProvider) : INewFeedTransactionHandler
{
    private readonly IUnitOfWorkProvider<FeedsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public Task HandleNewPersonalFeedTransactionAsync(ValidatedTransaction<NewPersonalFeedPayload> newPersonalFeedTransaction)
    {
        using var readonlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();

        // var blockchainState = readonlyUnitOfWork

        var newPersonalFeedPayload = newPersonalFeedTransaction.Payload;

        // var feed = new Feed(
        //     newPersonalFeedPayload.FeedId,
        //     newPersonalFeedPayload.Title,
        //     newPersonalFeedPayload.FeedType);

        return Task.CompletedTask;
    }
}
