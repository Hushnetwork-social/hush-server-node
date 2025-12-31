using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Transaction handler that processes validated LeaveGroupFeed transactions.
/// Updates the leaving user's participant record and handles last-admin scenario.
/// </summary>
public class LeaveGroupFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache)
    : ILeaveGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;

    public async Task HandleLeaveGroupFeedTransactionAsync(ValidatedTransaction<LeaveGroupFeedPayload> transaction)
    {
        var payload = transaction.Payload;
        var currentBlock = this._blockchainCache.LastBlockIndex;
        var leavingUserAddress = payload.LeavingUserPublicAddress;

        // Get the participant to check if they're an admin
        var participant = await this._feedsStorageService
            .GetGroupFeedParticipantAsync(payload.FeedId, leavingUserAddress);

        if (participant == null)
        {
            // Should not happen as validation checked this, but guard anyway
            return;
        }

        // Check if this is the last admin leaving
        bool isLastAdmin = false;
        if (participant.ParticipantType == ParticipantType.Admin)
        {
            var adminCount = await this._feedsStorageService.GetAdminCountAsync(payload.FeedId);
            isLastAdmin = (adminCount == 1);
        }

        // Update the participant's leave status
        await this._feedsStorageService.UpdateParticipantLeaveStatusAsync(
            payload.FeedId,
            leavingUserAddress,
            currentBlock);

        // If last admin leaving, soft-delete the group
        if (isLastAdmin)
        {
            await this._feedsStorageService.MarkGroupFeedDeletedAsync(payload.FeedId);
            // No key rotation needed for deleted group
        }
        // Key rotation note: For non-deletion cases, the remaining admins should
        // submit a GroupFeedKeyRotationPayload transaction to distribute new
        // encrypted keys to remaining members (excluding the leaving user).
    }
}
