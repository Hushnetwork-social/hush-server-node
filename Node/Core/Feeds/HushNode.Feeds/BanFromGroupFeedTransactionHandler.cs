using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler for BanFromGroupFeed transactions.
/// Sets participant status to Banned and triggers key rotation.
/// Unlike Block (FEAT-009), Ban is a cryptographic operation - banned member
/// will NOT receive new encryption keys and cannot decrypt future messages.
/// </summary>
public class BanFromGroupFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IKeyRotationService keyRotationService)
    : IBanFromGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IKeyRotationService _keyRotationService = keyRotationService;

    public async Task HandleBanFromGroupFeedTransactionAsync(ValidatedTransaction<BanFromGroupFeedPayload> banTransaction)
    {
        var payload = banTransaction.Payload;

        // Step 1: Update participant status from Member/Blocked to Banned
        await this._feedsStorageService.UpdateParticipantTypeAsync(
            payload.FeedId,
            payload.BannedUserPublicAddress,
            ParticipantType.Banned);

        // Step 2: Trigger key rotation to exclude banned member from future key distribution
        // The leavingMemberAddress parameter ensures the banned member is excluded from the new key
        await this._keyRotationService.TriggerRotationAsync(
            payload.FeedId,
            RotationTrigger.Ban,
            joiningMemberAddress: null,
            leavingMemberAddress: payload.BannedUserPublicAddress);
    }
}
