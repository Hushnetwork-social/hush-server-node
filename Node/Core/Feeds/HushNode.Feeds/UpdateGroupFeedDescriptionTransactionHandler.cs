using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler for UpdateGroupFeedDescription transactions.
/// Updates the group's description.
/// Does NOT trigger key rotation - metadata change only.
/// </summary>
public class UpdateGroupFeedDescriptionTransactionHandler(
    IFeedsStorageService feedsStorageService)
    : IUpdateGroupFeedDescriptionTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public async Task HandleUpdateGroupFeedDescriptionTransactionAsync(ValidatedTransaction<UpdateGroupFeedDescriptionPayload> updateDescriptionTransaction)
    {
        var payload = updateDescriptionTransaction.Payload;

        // Update the group's description
        await this._feedsStorageService.UpdateGroupFeedDescriptionAsync(
            payload.FeedId,
            payload.NewDescription);
    }
}
