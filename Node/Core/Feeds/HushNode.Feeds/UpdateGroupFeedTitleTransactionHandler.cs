using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler for UpdateGroupFeedTitle transactions.
/// Updates the group's title.
/// Does NOT trigger key rotation - metadata change only.
/// </summary>
public class UpdateGroupFeedTitleTransactionHandler(
    IFeedsStorageService feedsStorageService)
    : IUpdateGroupFeedTitleTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public async Task HandleUpdateGroupFeedTitleTransactionAsync(ValidatedTransaction<UpdateGroupFeedTitlePayload> updateTitleTransaction)
    {
        var payload = updateTitleTransaction.Payload;

        // Update the group's title
        await this._feedsStorageService.UpdateGroupFeedTitleAsync(
            payload.FeedId,
            payload.NewTitle);
    }
}
