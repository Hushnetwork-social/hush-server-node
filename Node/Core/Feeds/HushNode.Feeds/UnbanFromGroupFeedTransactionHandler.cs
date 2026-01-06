using HushNode.Feeds.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler for UnbanFromGroupFeed transactions.
/// Restores participant status to Member and triggers key rotation.
/// Unlike Unblock (FEAT-009), Unban is a cryptographic operation - unbanned member
/// will receive new encryption keys and can decrypt future messages.
/// NOTE: Unbanned member CANNOT read messages from the ban period (security by design).
/// </summary>
public class UnbanFromGroupFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IKeyRotationService keyRotationService)
    : IUnbanFromGroupFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IKeyRotationService _keyRotationService = keyRotationService;

    public async Task HandleUnbanFromGroupFeedTransactionAsync(ValidatedTransaction<UnbanFromGroupFeedPayload> unbanTransaction)
    {
        var payload = unbanTransaction.Payload;

        // Step 1: Update participant status from Banned to Member
        await this._feedsStorageService.UpdateParticipantTypeAsync(
            payload.FeedId,
            payload.UnbannedUserPublicAddress,
            ParticipantType.Member);

        // Step 2: Trigger key rotation to include unbanned member in key distribution
        // The joiningMemberAddress parameter ensures the unbanned member receives the new key
        await this._keyRotationService.TriggerAndPersistRotationAsync(
            payload.FeedId,
            RotationTrigger.Unban,
            joiningMemberAddress: payload.UnbannedUserPublicAddress,
            leavingMemberAddress: null);
    }
}
