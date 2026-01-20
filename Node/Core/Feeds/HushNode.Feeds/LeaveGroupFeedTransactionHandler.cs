using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Transaction handler that processes validated LeaveGroupFeed transactions.
/// Updates the leaving user's participant record, handles last-admin scenario,
/// and triggers key rotation to exclude the leaving member from future keys.
/// </summary>
public class LeaveGroupFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IKeyRotationService keyRotationService,
    IUserFeedsCacheService userFeedsCacheService)
    : ILeaveGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IKeyRotationService _keyRotationService = keyRotationService;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;

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
        else
        {
            // Trigger key rotation to exclude leaving member from future key distribution
            // This ensures the leaving user can no longer decrypt messages sent after they leave
            await this._keyRotationService.TriggerAndPersistRotationAsync(
                payload.FeedId,
                RotationTrigger.Leave,
                joiningMemberAddress: null,
                leavingMemberAddress: leavingUserAddress);
        }

        // Update the leaving user's feed list cache (FEAT-049)
        // Cache update is fire-and-forget - failure does not affect the transaction
        await this._userFeedsCacheService.RemoveFeedFromUserCacheAsync(leavingUserAddress, payload.FeedId);
    }
}
