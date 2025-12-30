using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler for DeleteGroupFeed transactions.
/// Soft-deletes the group (marks as deleted, preserves data).
/// After deletion:
/// - No new messages can be sent to the group
/// - Existing message history remains accessible
/// </summary>
public class DeleteGroupFeedTransactionHandler(
    IFeedsStorageService feedsStorageService)
    : IDeleteGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public async Task HandleDeleteGroupFeedTransactionAsync(ValidatedTransaction<DeleteGroupFeedPayload> deleteGroupFeedTransaction)
    {
        var payload = deleteGroupFeedTransaction.Payload;

        // Soft-delete the group (mark as deleted, preserve data)
        await this._feedsStorageService.MarkGroupFeedDeletedAsync(payload.FeedId);
    }
}
